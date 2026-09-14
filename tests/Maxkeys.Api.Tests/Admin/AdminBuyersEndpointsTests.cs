using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Buyers;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>
/// Covers the admin-buyers spec: Buyer Listing Grouped By Email, Key
/// Exposure in Buyer View, Admin Buyers Authorization.
/// </summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AdminBuyersEndpointsTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly Hs256ApiTestFixture _factory;

    public AdminBuyersEndpointsTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/admin/buyers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden()
    {
        var response = await AdminClient(NonAdminSub).GetAsync("/admin/buyers");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Listing_never_exposes_a_key_code_only_the_assigned_count()
    {
        var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
        var (orderId, code) = await SeedDeliveredOrderAsync(buyerEmail);

        var response = await AdminClient().GetAsync($"/admin/buyers?email={buyerEmail}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(code, rawJson);

        var page = await response.Content.ReadFromJsonAsync<BuyersPage>();
        var buyer = Assert.Single(page!.Items, b => b.Email == buyerEmail);
        var order = Assert.Single(buyer.Orders, o => o.Id == orderId);
        var item = Assert.Single(order.Items);
        Assert.Equal(1, item.AssignedKeys);
    }

    private async Task<(Guid OrderId, string Code)> SeedDeliveredOrderAsync(string buyerEmail)
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;
        var code = "SECRET-CODE-1";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(
            null, buyerEmail, [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var item = order.Items.Single();
        var (blob, version) = cipher.Encrypt(code);
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
        db.Keys.Add(key); // client-generated Id — must be added explicitly (see AttachKeyToOrderItem)
        order.AttachKey(item.Id, key, now);
        await db.SaveChangesAsync();

        return (order.Id, code);
    }

    private HttpClient AdminClient(string? sub = null)
    {
        var token = TestTokens.CreateHs256(
            sub ?? Hs256ApiTestFixture.AdminSub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
