using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>Covers <c>POST /admin/orders/{id}/items/{itemId}/keys</c> (fulfillment spec: Key Attachment; admin manual key load).</summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AttachKeyEndpointTests
{
    private readonly Hs256ApiTestFixture _factory;

    public AttachKeyEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unknown_order_returns_404()
    {
        var response = await AdminClient().PostAsJsonAsync(
            $"/admin/orders/{Guid.NewGuid()}/items/{Guid.NewGuid()}/keys", new AttachKeyRequest("CODE-1"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Attaching_to_an_incomplete_item_persists_an_assigned_vault_key()
    {
        var (orderId, itemId, variantId) = await SeedAwaitingFulfillmentOrderAsync(vaultEnabled: false);

        var response = await AdminClient().PostAsJsonAsync(
            $"/admin/orders/{orderId}/items/{itemId}/keys", new AttachKeyRequest("CODE-1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AttachKeyResponse>();
        Assert.Equal(nameof(OrderStatus.KeysAssigned), body!.OrderStatus);
        Assert.Equal(1, Assert.Single(body.Items).Assigned);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = await db.Keys.SingleAsync(k => k.OrderItemId == itemId);
        Assert.Equal(KeyStatus.Assigned, key.Status);
        Assert.Equal(variantId, key.ProductVariantId);
    }

    [Fact]
    public async Task Attaching_to_a_full_item_returns_409()
    {
        var (orderId, itemId, _) = await SeedAwaitingFulfillmentOrderAsync(vaultEnabled: false);
        await AdminClient().PostAsJsonAsync($"/admin/orders/{orderId}/items/{itemId}/keys", new AttachKeyRequest("CODE-1"));

        var response = await AdminClient().PostAsJsonAsync(
            $"/admin/orders/{orderId}/items/{itemId}/keys", new AttachKeyRequest("CODE-2"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Cannot attach a key unless the order is awaiting fulfillment.", problem!.Detail);
    }

    private async Task<(Guid OrderId, Guid ItemId, Guid VariantId)> SeedAwaitingFulfillmentOrderAsync(bool vaultEnabled)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product($"p-{Guid.NewGuid():N}", "Manual Product", $"platform-{Guid.NewGuid():N}");
        product.SetVaultEnabled(vaultEnabled);
        db.Products.Add(product);
        var variant = new ProductVariant(product.Id, 1_000m, "ARS", region: "US", edition: "Standard");
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(variant.Id, "Manual Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return (order.Id, order.Items.Single().Id, variant.Id);
    }

    private HttpClient AdminClient()
    {
        var token = TestTokens.CreateHs256(
            Hs256ApiTestFixture.AdminSub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
