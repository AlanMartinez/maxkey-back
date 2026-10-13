using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>
/// Covers the admin bank-transfer queue: <c>GET /admin/orders?status=awaiting-transfer</c>,
/// <c>POST /admin/orders/{id}/approve-transfer</c> and <c>/reject-transfer</c>, plus the
/// <c>paymentMethod</c>/<c>buyerPhone</c> fields on the admin order responses.
/// </summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class BankTransferAdminEndpointTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private readonly Hs256ApiTestFixture _factory;

    public BankTransferAdminEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("approve-transfer")]
    [InlineData("reject-transfer")]
    public async Task Anonymous_and_non_admin_requests_are_rejected(string action)
    {
        var anonymous = await _factory.CreateClient().PostAsync($"/admin/orders/{Guid.NewGuid()}/{action}", content: null);
        var nonAdmin = await AdminClient(NonAdminSub).PostAsync($"/admin/orders/{Guid.NewGuid()}/{action}", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonAdmin.StatusCode);
    }

    [Theory]
    [InlineData("approve-transfer")]
    [InlineData("reject-transfer")]
    public async Task Unknown_order_returns_404(string action)
    {
        var response = await AdminClient().PostAsync($"/admin/orders/{Guid.NewGuid()}/{action}", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Awaiting_transfer_filter_lists_only_transfer_orders_oldest_first()
    {
        var now = DateTimeOffset.UtcNow;
        var newer = await SeedTransferOrderAsync(now.AddMinutes(-1));
        var older = await SeedTransferOrderAsync(now.AddMinutes(-10));

        using var body = JsonDocument.Parse(await AdminClient().GetStringAsync("/admin/orders?status=awaiting-transfer"));

        var orders = body.RootElement.EnumerateArray().ToList();
        Assert.All(orders, o => Assert.Equal("AwaitingTransferValidation", o.GetProperty("status").GetString()));
        var ids = orders.Select(o => o.GetProperty("id").GetGuid()).ToList();
        Assert.True(ids.IndexOf(older) >= 0 && ids.IndexOf(older) < ids.IndexOf(newer));

        var listed = orders.Single(o => o.GetProperty("id").GetGuid() == older);
        Assert.Equal("bank-transfer", listed.GetProperty("paymentMethod").GetString());
        Assert.Equal("+5491112345678", listed.GetProperty("buyerPhone").GetString());
        Assert.Equal(1_000m, listed.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("ARS", listed.GetProperty("currency").GetString());
        Assert.Equal(now.AddMinutes(-10).ToUnixTimeSeconds(), listed.GetProperty("createdAt").GetDateTimeOffset().ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Default_listing_still_shows_only_the_fulfillment_queue()
    {
        var transferOrderId = await SeedTransferOrderAsync(DateTimeOffset.UtcNow);
        var fulfillmentOrderId = await SeedAwaitingFulfillmentOrderAsync();

        using var body = JsonDocument.Parse(await AdminClient().GetStringAsync("/admin/orders"));

        var orders = body.RootElement.EnumerateArray().ToList();
        Assert.DoesNotContain(orders, o => o.GetProperty("id").GetGuid() == transferOrderId);
        var listed = orders.Single(o => o.GetProperty("id").GetGuid() == fulfillmentOrderId);
        Assert.Equal("mercadopago", listed.GetProperty("paymentMethod").GetString());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("buyerPhone").ValueKind);
    }

    [Fact]
    public async Task Approve_returns_paid_order_and_enqueues_order_approved()
    {
        var orderId = await SeedTransferOrderAsync(DateTimeOffset.UtcNow);

        var response = await AdminClient().PostAsync($"/admin/orders/{orderId}/approve-transfer", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(orderId, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Paid", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("bank-transfer", body.RootElement.GetProperty("paymentMethod").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.RootElement.GetProperty("paidAt").ValueKind);
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("createdAt").ValueKind);
        Assert.Equal(1_000m, body.RootElement.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("ARS", body.RootElement.GetProperty("currency").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = await new OutboxEventReader(db).ListByOrderIdAsync(orderId);
        Assert.Single(events, evt => evt.Type == OutboxEventTypes.OrderApproved);

        var again = await AdminClient().PostAsync($"/admin/orders/{orderId}/approve-transfer", content: null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var reject = await AdminClient().PostAsync($"/admin/orders/{orderId}/reject-transfer", content: null);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
    }

    [Fact]
    public async Task Reject_returns_cancelled_order_and_cannot_be_approved_afterwards()
    {
        var orderId = await SeedTransferOrderAsync(DateTimeOffset.UtcNow);

        var response = await AdminClient().PostAsync($"/admin/orders/{orderId}/reject-transfer", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Cancelled", body.RootElement.GetProperty("status").GetString());

        var approve = await AdminClient().PostAsync($"/admin/orders/{orderId}/approve-transfer", content: null);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
    }

    [Fact]
    public async Task Approve_on_a_mercado_pago_order_returns_409()
    {
        var orderId = await SeedAwaitingFulfillmentOrderAsync();

        var response = await AdminClient().PostAsync($"/admin/orders/{orderId}/approve-transfer", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Order_detail_includes_payment_method_and_buyer_phone()
    {
        var orderId = await SeedTransferOrderAsync(DateTimeOffset.UtcNow);

        using var body = JsonDocument.Parse(await AdminClient().GetStringAsync($"/admin/orders/{orderId}"));

        Assert.Equal("bank-transfer", body.RootElement.GetProperty("paymentMethod").GetString());
        Assert.Equal("+5491112345678", body.RootElement.GetProperty("buyerPhone").GetString());
        Assert.Equal("AwaitingTransferValidation", body.RootElement.GetProperty("status").GetString());
    }

    private async Task<Guid> SeedTransferOrderAsync(DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)],
            createdAt,
            PaymentMethod.BankTransfer,
            "+54 9 11 1234-5678");
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return order.Id;
    }

    private async Task<Guid> SeedAwaitingFulfillmentOrderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
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
