using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Orders;

/// <summary>
/// End-to-end cover for the order-ownership model: the address submitted at
/// checkout is the single source of truth for who owns an order, and only a
/// provider-verified email may take that ownership.
/// <para>
/// These run through a real signed token on purpose. The unit tests in
/// <see cref="VerifiedEmailResolverTests"/> build a <c>ClaimsPrincipal</c> by hand,
/// so they cannot catch the case where Supabase's nested
/// <c>app_metadata.provider</c> does not survive JWT validation in the shape the
/// resolver parses — which would fail closed and silently stop every buyer from
/// claiming their guest order.
/// </para>
/// </summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class OrderOwnershipEndpointTests
{
    private readonly Hs256ApiTestFixture _factory;

    public OrderOwnershipEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Google_token_claims_a_guest_order_with_a_matching_email()
    {
        var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
        var orderId = await SeedGuestOrderAsync(buyerEmail);
        var userId = Guid.NewGuid();

        var response = await BuyerClient(userId, buyerEmail).GetAsync("/me/orders");

        response.EnsureSuccessStatusCode();
        Assert.Contains(orderId, await ReadOrderIdsAsync(response));
        Assert.Equal(userId, await ReadOwnerAsync(orderId));
    }

    [Fact]
    public async Task Email_provider_token_cannot_claim_a_guest_order()
    {
        // The core of the finding: Supabase's public /auth/v1/signup can mint a token for
        // an address its owner never confirmed. Such a token must never take ownership of
        // the real buyer's order, because ownership is what unlocks the reveal endpoint —
        // the only path that discloses a key code (ADR-14).
        var buyerEmail = $"victim-{Guid.NewGuid():N}@example.com";
        var orderId = await SeedGuestOrderAsync(buyerEmail);

        var response = await BuyerClient(Guid.NewGuid(), buyerEmail, provider: "email").GetAsync("/me/orders");

        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain(orderId, await ReadOrderIdsAsync(response));
        Assert.Null(await ReadOwnerAsync(orderId));
    }

    [Fact]
    public async Task Token_without_app_metadata_cannot_claim_a_guest_order()
    {
        var buyerEmail = $"victim-{Guid.NewGuid():N}@example.com";
        var orderId = await SeedGuestOrderAsync(buyerEmail);

        var token = TestTokens.CreateHs256(
            Guid.NewGuid().ToString(),
            Hs256ApiTestFixture.Issuer,
            Hs256ApiTestFixture.Audience,
            Hs256ApiTestFixture.Hs256Secret,
            lifetime: null,
            extraClaims: [new System.Security.Claims.Claim("email", buyerEmail)]);

        var response = await ClientWith(token).GetAsync("/me/orders");

        response.EnsureSuccessStatusCode();
        Assert.Null(await ReadOwnerAsync(orderId));
    }

    [Fact]
    public async Task Authenticated_checkout_with_a_different_email_creates_an_unowned_order()
    {
        // "Bought for someone else": the order must behave exactly like a guest order so
        // the recipient claims it on login — the same address the delivery email goes to.
        var variantId = await SeedActiveVariantAsync();
        var recipientEmail = $"recipient-{Guid.NewGuid():N}@example.com";

        await BuyerClient(Guid.NewGuid(), $"payer-{Guid.NewGuid():N}@example.com")
            .PostAsJsonAsync("/checkout/orders", new
            {
                email = recipientEmail,
                items = new[] { new { variantId, quantity = 1 } },
            });

        Assert.Null(await ReadOwnerByEmailAsync(recipientEmail));
    }

    [Fact]
    public async Task Authenticated_checkout_with_its_own_email_links_the_order_immediately()
    {
        var variantId = await SeedActiveVariantAsync();
        var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
        var userId = Guid.NewGuid();

        await BuyerClient(userId, buyerEmail).PostAsJsonAsync("/checkout/orders", new
        {
            email = buyerEmail,
            items = new[] { new { variantId, quantity = 1 } },
        });

        Assert.Equal(userId, await ReadOwnerByEmailAsync(buyerEmail));
    }

    [Fact]
    public async Task Authenticated_checkout_matches_its_own_email_case_insensitively()
    {
        var variantId = await SeedActiveVariantAsync();
        var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
        var userId = Guid.NewGuid();

        await BuyerClient(userId, buyerEmail.ToUpperInvariant()).PostAsJsonAsync("/checkout/orders", new
        {
            email = buyerEmail,
            items = new[] { new { variantId, quantity = 1 } },
        });

        Assert.Equal(userId, await ReadOwnerByEmailAsync(buyerEmail));
    }

    private static async Task<IReadOnlyList<Guid>> ReadOrderIdsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray()
            .Select(order => order.GetProperty("id").GetGuid())
            .ToList();
    }

    private async Task<Guid?> ReadOwnerAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).UserId;
    }

    private async Task<Guid?> ReadOwnerByEmailAsync(string buyerEmail)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Orders.AsNoTracking().SingleAsync(o => o.BuyerEmail == buyerEmail)).UserId;
    }

    private async Task<Guid> SeedGuestOrderAsync(string buyerEmail)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(
            null, buyerEmail, [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private async Task<Guid> SeedActiveVariantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product($"p-{Guid.NewGuid():N}", "Ownership Test Product", $"platform-{Guid.NewGuid():N}");
        db.Products.Add(product);
        var variant = new ProductVariant(product.Id, 100m, "ARS");
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        return variant.Id;
    }

    private HttpClient BuyerClient(Guid userId, string email, string provider = "google") =>
        ClientWith(TestTokens.CreateSupabaseHs256(
            userId.ToString(),
            Hs256ApiTestFixture.Issuer,
            Hs256ApiTestFixture.Audience,
            Hs256ApiTestFixture.Hs256Secret,
            email,
            provider));

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
