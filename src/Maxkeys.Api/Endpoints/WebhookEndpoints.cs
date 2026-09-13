using Maxkeys.Application.Payments;
using Maxkeys.Infrastructure.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Mercado Pago payment-notification intake (design section 6b, payments-webhook
/// spec). Check order matters and each check is zero-I/O until it passes: kill
/// switch first (spec "Kill Switch"), then signature (spec "Signature
/// Validation Before Any I/O"); dedupe/authoritative fetch/state transition
/// live in <see cref="ProcessPaymentNotification"/>.
/// </summary>
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/mercadopago", async (
            HttpContext httpContext,
            [FromQuery(Name = "data.id")] string? dataId,
            [FromQuery] string? type,
            IOptionsMonitor<MercadoPagoOptions> options,
            MercadoPagoSignatureValidator signatureValidator,
            ProcessPaymentNotification useCase,
            CancellationToken cancellationToken) =>
        {
            if (!options.CurrentValue.WebhookEnabled)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Webhook disabled");
            }

            var xSignature = httpContext.Request.Headers["x-signature"].ToString();
            var xRequestId = httpContext.Request.Headers["x-request-id"].ToString();

            if (!signatureValidator.IsValid(xSignature, xRequestId, dataId))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid signature");
            }

            if (!string.Equals(type, "payment", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok();
            }

            await useCase.ExecuteAsync(xRequestId, dataId!, cancellationToken);
            return Results.Ok();
        });

        return app;
    }
}
