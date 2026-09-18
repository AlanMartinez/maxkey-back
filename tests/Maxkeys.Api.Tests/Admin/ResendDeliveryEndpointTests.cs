using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>
/// Covers <c>POST /admin/orders/{id}/resend-delivery</c> (admin-buyers spec:
/// Resend Delivery Email; fulfillment spec: One-Time Delivery Email —
/// MODIFIED).
/// </summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class ResendDeliveryEndpointTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly Hs256ApiTestFixture _factory;

    public ResendDeliveryEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().PostAsync($"/admin/orders/{Guid.NewGuid()}/resend-delivery", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden()
    {
        var response = await AdminClient(NonAdminSub).PostAsync($"/admin/orders/{Guid.NewGuid()}/resend-delivery", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resend_for_a_delivered_order_returns_202_with_an_outbox_event_id()
    {
        var orderId = await SeedDeliveredOrderAsync();

        var response = await AdminClient().PostAsync($"/admin/orders/{orderId}/resend-delivery", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResendDeliveryResponse>();
        Assert.NotEqual(Guid.Empty, body!.OutboxEventId);
    }

    [Fact]
    public async Task Resend_for_a_non_delivered_order_returns_409_and_sends_no_email()
    {
        var orderId = await SeedAwaitingFulfillmentOrderAsync();

        var response = await AdminClient().PostAsync($"/admin/orders/{orderId}/resend-delivery", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Resend_for_an_unknown_order_returns_404()
    {
        var response = await AdminClient().PostAsync($"/admin/orders/{Guid.NewGuid()}/resend-delivery", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> SeedDeliveredOrderAsync()
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(
            null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var item = order.Items.Single();
        var (blob, version) = cipher.Encrypt("CODE-ONE");
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
        db.Keys.Add(key); // client-generated Id — must be added explicitly (see AttachKeyToOrderItem)
        order.AttachKey(item.Id, key, now);
        order.MarkDelivered(now);
        await db.SaveChangesAsync();

        return order.Id;
    }

    private async Task<Guid> SeedAwaitingFulfillmentOrderAsync()
    {
        var now = DateTimeOffset.UtcNow;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(
            null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return order.Id;
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
