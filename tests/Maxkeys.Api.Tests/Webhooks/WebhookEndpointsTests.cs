using System.Net;
using System.Security.Cryptography;
using System.Text;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Domain.Payments;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Webhooks;

/// <summary>
/// End-to-end webhook intake tests against a real host + Postgres, using
/// <see cref="WebhooksApiTestFixture"/>'s <see cref="FakeMercadoPagoHandler"/>
/// for the authoritative payment fetch (payments-webhook spec).
/// </summary>
[Collection(WebhooksApiCollection.Name)]
public sealed class WebhookEndpointsTests
{
    private readonly WebhooksApiTestFixture _factory;

    public WebhookEndpointsTests(WebhooksApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Tampered_signature_is_rejected_with_no_state_change()
    {
        var order = await SeedPendingOrderAsync();
        const string paymentId = "700100200";
        const string requestId = "tampered-request-1";
        _factory.Handler.SetPaymentResponse(paymentId, order.Id.ToString(), order.TotalAmount, order.Currency, "approved");

        var client = _factory.CreateClient();
        var request = BuildWebhookRequest(paymentId, requestId, ts: "1704908010", v1: new string('0', 64));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoDedupeRowAsync(requestId);
    }

    [Fact]
    public async Task Missing_signature_header_is_rejected_with_no_state_change()
    {
        const string paymentId = "700100201";
        const string requestId = "missing-signature-request";

        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/mercadopago?data.id={paymentId}&type=payment");
        request.Headers.Add("x-request-id", requestId);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoDedupeRowAsync(requestId);
    }

    [Fact]
    public async Task Exact_replay_of_the_same_request_id_is_a_no_op()
    {
        var order = await SeedPendingOrderAsync();
        const string paymentId = "700100202";
        const string requestId = "replay-request-1";
        _factory.Handler.SetPaymentResponse(paymentId, order.Id.ToString(), order.TotalAmount, order.Currency, "approved");

        var client = _factory.CreateClient();

        var first = await client.SendAsync(BuildWebhookRequest(paymentId, requestId));
        Assert.True(first.IsSuccessStatusCode);

        var second = await client.SendAsync(BuildWebhookRequest(paymentId, requestId));
        Assert.True(second.IsSuccessStatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var persisted = await db.Orders.SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Paid, persisted.Status);
        Assert.Equal(1, await db.OutboxEvents.CountAsync(e => e.Type == OutboxEventTypes.OrderApproved));
        Assert.Equal(1, await db.ProcessedWebhookNotifications.CountAsync(n => n.RequestId == requestId));
    }

    private async Task<Order> SeedPendingOrderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(
            userId: null,
            buyerEmail: $"buyer-{Guid.NewGuid():N}@example.com",
            lines: [new OrderLine(Guid.NewGuid(), "Product", "Variant", 100m, 1)],
            now: DateTimeOffset.UtcNow);

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return order;
    }

    private async Task AssertNoDedupeRowAsync(string requestId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.ProcessedWebhookNotifications.AnyAsync(n => n.RequestId == requestId));
    }

    private static HttpRequestMessage BuildWebhookRequest(string paymentId, string requestId, string? ts = null, string? v1 = null)
    {
        ts ??= "1704908010";
        v1 ??= ComputeHex(paymentId, requestId, ts);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/mercadopago?data.id={paymentId}&type=payment");
        request.Headers.Add("x-signature", $"ts={ts},v1={v1}");
        request.Headers.Add("x-request-id", requestId);
        return request;
    }

    private static string ComputeHex(string dataId, string requestId, string ts)
    {
        var manifest = $"id:{dataId};request-id:{requestId};ts:{ts};";
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhooksApiTestFixture.WebhookSecret), Encoding.UTF8.GetBytes(manifest));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
