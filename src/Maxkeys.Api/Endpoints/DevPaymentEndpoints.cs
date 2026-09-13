using System.Globalization;
using System.Net;
using Maxkeys.Application.Checkout;
using Maxkeys.Application.Payments;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Local demo replacement for the Mercado Pago checkout page (docs/local-demo.md).
/// Mapped by <c>Program.cs</c> only when the host runs in Development AND
/// <c>Payments:Mode</c> is <c>Fake</c>; in every other configuration these
/// routes do not exist (404). Approval goes through the same
/// <see cref="ProcessPaymentNotification"/> use case the real webhook uses —
/// nothing here touches the database directly — so the order transitions
/// (<c>Paid</c>, outbox event, <c>AwaitingFulfillment</c>, operator email) are
/// exactly the production ones.
/// </summary>
public static class DevPaymentEndpoints
{
    public static IEndpointRouteBuilder MapDevPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dev/payments");

        group.MapGet("/{orderId:guid}", async (
            Guid orderId,
            GetOrderStatus getOrderStatus,
            IOptionsMonitor<MercadoPagoOptions> options,
            CancellationToken cancellationToken) =>
        {
            var order = await getOrderStatus.ExecuteAsync(orderId, cancellationToken);
            if (order is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found");
            }

            var html = RenderPage(order, BuildReturnUrl(options.CurrentValue, orderId));
            return Results.Content(html, "text/html; charset=utf-8");
        });

        group.MapPost("/{orderId:guid}/approve", async (
            Guid orderId,
            HttpContext httpContext,
            GetOrderStatus getOrderStatus,
            FakePaymentGateway gateway,
            ProcessPaymentNotification processPaymentNotification,
            IOptionsMonitor<MercadoPagoOptions> options,
            CancellationToken cancellationToken) =>
        {
            var order = await getOrderStatus.ExecuteAsync(orderId, cancellationToken);
            if (order is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found");
            }

            var payment = gateway.Approve(orderId, order.TotalAmount, order.Currency);
            var requestId = $"dev-{orderId}-{Guid.NewGuid():N}";
            await processPaymentNotification.ExecuteAsync(requestId, payment.Id, cancellationToken);

            httpContext.Response.Headers.Location = BuildReturnUrl(options.CurrentValue, orderId);
            return Results.StatusCode(StatusCodes.Status303SeeOther);
        });

        return app;
    }

    private static string BuildReturnUrl(MercadoPagoOptions options, Guid orderId) =>
        options.FakeReturnUrl.Replace("{orderId}", orderId.ToString(), StringComparison.Ordinal);

    private static string RenderPage(CheckoutStatusResult order, string returnUrl)
    {
        var orderId = WebUtility.HtmlEncode(order.OrderId.ToString());
        var status = WebUtility.HtmlEncode(order.Status.ToString());
        var amount = WebUtility.HtmlEncode($"{order.TotalAmount.ToString("N2", CultureInfo.InvariantCulture)} {order.Currency}");
        var buyer = WebUtility.HtmlEncode(order.BuyerEmail);
        var encodedReturnUrl = WebUtility.HtmlEncode(returnUrl);

        var action = order.Status == OrderStatus.Pending
            ? $"""
              <form method="post" action="/dev/payments/{orderId}/approve">
                <button type="submit">Approve payment</button>
              </form>
              """
            : """<p class="note">This order is no longer pending, so there is nothing to approve.</p>""";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Fake payment - order {{orderId}}</title>
              <style>
                body { font-family: system-ui, sans-serif; background: #f4f4f5; color: #18181b; margin: 0; padding: 2rem; }
                main { max-width: 32rem; margin: 0 auto; background: #fff; border-radius: 12px; padding: 2rem; box-shadow: 0 1px 3px rgba(0, 0, 0, .1); }
                h1 { font-size: 1.25rem; margin-top: 0; }
                dl { display: grid; grid-template-columns: max-content 1fr; gap: .5rem 1rem; }
                dt { color: #71717a; }
                dd { margin: 0; word-break: break-all; }
                button { background: #2563eb; color: #fff; border: 0; border-radius: 8px; padding: .75rem 1.25rem; font-size: 1rem; cursor: pointer; }
                button:hover { background: #1d4ed8; }
                .banner { background: #fef3c7; color: #92400e; border-radius: 8px; padding: .75rem 1rem; font-size: .875rem; margin-bottom: 1.5rem; }
                .note { color: #71717a; }
                a { color: #2563eb; }
              </style>
            </head>
            <body>
              <main>
                <div class="banner">Local demo mode. This page replaces the Mercado Pago checkout; no real payment happens.</div>
                <h1>Fake payment</h1>
                <dl>
                  <dt>Order</dt><dd>{{orderId}}</dd>
                  <dt>Buyer</dt><dd>{{buyer}}</dd>
                  <dt>Amount</dt><dd>{{amount}}</dd>
                  <dt>Status</dt><dd>{{status}}</dd>
                </dl>
                {{action}}
                <p><a href="{{encodedReturnUrl}}">Back to the store</a></p>
              </main>
            </body>
            </html>
            """;
    }
}
