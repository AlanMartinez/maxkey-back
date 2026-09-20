using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Fulfillment;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>Covers <c>POST /admin/orders/{id}/assign-keys</c> (admin-key-delivery-gate spec: decision 3, "Asignar").</summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AssignKeysEndpointTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private readonly Hs256ApiTestFixture _factory;

    public AssignKeysEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().PostAsync($"/admin/orders/{Guid.NewGuid()}/assign-keys", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden()
    {
        var response = await AdminClient(NonAdminSub).PostAsync($"/admin/orders/{Guid.NewGuid()}/assign-keys", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_order_returns_404()
    {
        var response = await AdminClient().PostAsync($"/admin/orders/{Guid.NewGuid()}/assign-keys", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Order_with_full_vault_stock_is_fully_assigned()
    {
        var (orderId, variantId) = await SeedAwaitingFulfillmentVaultOrderAsync(quantity: 1);
        await LoadStockAsync(variantId, "CODE-1");

        var response = await AdminClient().PostAsync($"/admin/orders/{orderId}/assign-keys", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssignKeysResponse>();
        Assert.True(body!.AllItemsComplete);
        Assert.Equal(nameof(OrderStatus.KeysAssigned), body.OrderStatus);
    }

    private async Task<(Guid OrderId, Guid VariantId)> SeedAwaitingFulfillmentVaultOrderAsync(int quantity)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product($"p-{Guid.NewGuid():N}", "Vault Product", $"platform-{Guid.NewGuid():N}");
        product.SetVaultEnabled(true);
        db.Products.Add(product);
        var variant = new ProductVariant(product.Id, 1_000m, "ARS", region: "AR", edition: "Standard");
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(variant.Id, "Vault Product", "Standard", 1_000m, quantity)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return (order.Id, variant.Id);
    }

    private async Task LoadStockAsync(Guid variantId, params string[] codes)
    {
        using var scope = _factory.Services.CreateScope();
        var loadVaultKeys = scope.ServiceProvider.GetRequiredService<LoadVaultKeys>();
        await loadVaultKeys.ExecuteAsync(variantId, codes, "seed-admin");
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
