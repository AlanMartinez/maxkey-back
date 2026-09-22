using System.Net;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Webhooks;

[Collection(WebhooksApiCollection.Name)]
public sealed class CheckoutReturnConfirmationTests
{
    private readonly WebhooksApiTestFixture _factory;

    public CheckoutReturnConfirmationTests(WebhooksApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Checkout_return_confirms_an_approved_payment_before_returning_status()
    {
        var (orderId, amount) = await SeedPendingOrderAsync();
        const string paymentId = "987654321";
        _factory.Handler.SetApprovedPaymentSearchResponse(orderId.ToString(), paymentId, amount, "ARS");
        _factory.Handler.SetPaymentResponse(paymentId, orderId.ToString(), amount, "ARS", "approved");

        var response = await _factory.CreateClient().PostAsync($"/checkout/orders/{orderId}/reconcile", null);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<CheckoutOrderStatusResponse>();
        Assert.NotNull(body);
        Assert.Equal("Paid", body.Status);
    }

    private async Task<(Guid OrderId, decimal Amount)> SeedPendingOrderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 2_500m, 2)], DateTimeOffset.UtcNow);
        order.AttachPreference($"pref-{Guid.NewGuid():N}");
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (order.Id, order.TotalAmount);
    }
}
