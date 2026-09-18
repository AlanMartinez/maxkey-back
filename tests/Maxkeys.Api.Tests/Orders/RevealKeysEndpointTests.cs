using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Tests.Orders;

/// <summary>Covers <c>POST /me/orders/{id}/items/{itemId}/keys/reveal</c> (admin-key-delivery-gate spec: decision 4, buyer reveal).</summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class RevealKeysEndpointTests
{
    // Must match Hs256ApiTestFixture's Keys:EncryptionKey, since this key round-trips
    // through the app's DI-registered KeyCipher on GET/POST reveal — not just this file's
    // own seeding helper (unlike sibling tests that reuse this same "ValidKeyBase64" name
    // for seeding only and never decrypt via the app).
    private const string ValidKeyBase64 = "8WkVdzuEWJDh35lKYZjBWeQhaadFl9ghCp3KRjZYgcY=";

    private readonly Hs256ApiTestFixture _factory;

    public RevealKeysEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().PostAsync(
            $"/me/orders/{Guid.NewGuid()}/items/{Guid.NewGuid()}/keys/reveal", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_order_returns_404()
    {
        var userId = Guid.NewGuid();
        var response = await BuyerClient(userId).PostAsync(
            $"/me/orders/{Guid.NewGuid()}/items/{Guid.NewGuid()}/keys/reveal", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_strangers_order_returns_404()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(owner);

        var response = await BuyerClient(stranger).PostAsync(
            $"/me/orders/{orderId}/items/{itemId}/keys/reveal", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Order_not_yet_delivered_returns_409()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedKeysAssignedOrderAsync(userId);

        var response = await BuyerClient(userId).PostAsync(
            $"/me/orders/{orderId}/items/{itemId}/keys/reveal", content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delivered_order_reveals_the_key_code()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(userId);

        var response = await BuyerClient(userId).PostAsync(
            $"/me/orders/{orderId}/items/{itemId}/keys/reveal", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RevealOrderItemKeysResponse>();
        Assert.Equal(["SEED-CODE"], body!.Codes);
    }

    private async Task<(Guid OrderId, Guid ItemId)> SeedKeysAssignedOrderAsync(Guid userId)
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(userId, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var item = order.Items.Single();
        var (blob, version) = cipher.Encrypt("SEED-CODE");
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
        db.Keys.Add(key);
        order.AttachKey(item.Id, key, now);
        await db.SaveChangesAsync();

        return (order.Id, item.Id);
    }

    private async Task<(Guid OrderId, Guid ItemId)> SeedDeliveredOrderAsync(Guid userId)
    {
        var (orderId, itemId) = await SeedKeysAssignedOrderAsync(userId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        order.MarkDelivered(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        return (orderId, itemId);
    }

    private HttpClient BuyerClient(Guid userId)
    {
        var token = TestTokens.CreateHs256(
            userId.ToString(), Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
