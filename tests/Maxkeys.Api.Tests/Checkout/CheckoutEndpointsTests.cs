using System.Net;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Checkout;

[Collection(ApiCollection.Name)]
public sealed class CheckoutEndpointsTests
{
    private readonly ApiTestFixture _factory;

    public CheckoutEndpointsTests(ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Missing_email_is_rejected_as_422_problem_details()
    {
        var variantId = await SeedActiveVariant();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/checkout/orders", new
        {
            email = "",
            items = new[] { new { variantId, quantity = 1 } },
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_variant_is_rejected_as_422_problem_details()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/checkout/orders", new
        {
            email = "buyer@example.com",
            items = new[] { new { variantId = Guid.NewGuid(), quantity = 1 } },
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Guest_checkout_without_a_configured_gateway_returns_503_problem_details()
    {
        var variantId = await SeedActiveVariant();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/checkout/orders", new
        {
            email = "buyer@example.com",
            items = new[] { new { variantId, quantity = 1 } },
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Body_with_an_extra_userId_field_is_ignored_order_stays_guest()
    {
        var variantId = await SeedActiveVariant();
        var client = _factory.CreateClient();
        var buyerEmail = $"guest-{Guid.NewGuid():N}@example.com";

        // "userId" is not a member of CheckoutOrderRequestBody; sending it must have no effect.
        await client.PostAsJsonAsync("/checkout/orders", new
        {
            email = buyerEmail,
            userId = Guid.NewGuid(),
            items = new[] { new { variantId, quantity = 1 } },
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = db.Orders.Single(o => o.BuyerEmail == buyerEmail);

        Assert.Null(order.UserId);
    }

    [Fact]
    public async Task Unknown_order_status_returns_404_problem_details()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/checkout/orders/{Guid.NewGuid()}/status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<Guid> SeedActiveVariant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product($"p-{Guid.NewGuid():N}", "Checkout Test Product", $"platform-{Guid.NewGuid():N}");
        db.Products.Add(product);
        var variant = new ProductVariant(product.Id, 100m, "ARS");
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        return variant.Id;
    }
}
