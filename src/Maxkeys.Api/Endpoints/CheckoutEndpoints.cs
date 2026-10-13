using System.Security.Claims;
using Maxkeys.Api.Auth;
using Maxkeys.Application.Checkout;
using Maxkeys.Application.Payments;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Public checkout endpoints (design section 7; cart-checkout spec). The
/// request body intentionally has no <c>UserId</c> property — linkage comes
/// only from the caller's optional bearer <c>sub</c> claim, validated by the
/// JWT bearer handler wired in <c>Program.cs</c> (design section 6a/6e, auth
/// spec "User Identity Linking"), and only when the submitted address matches
/// the token's own (see <see cref="ResolveUserId"/>): the submitted address is
/// the single source of truth for who ends up owning the order.
/// <see cref="OptionalBearerFilter"/> rejects a
/// present-but-invalid bearer with 401 instead of silently falling back to a
/// guest order (design section 6e); a request with no header at all still
/// proceeds as a guest.
/// <para>
/// <c>paymentMethod</c> is optional and defaults to <c>"mercadopago"</c>, so existing
/// clients are unchanged; <c>"bank-transfer"</c> (with a <c>phone</c>) creates an order
/// awaiting admin validation instead of a Mercado Pago handoff, using the account details
/// served by <c>GET /checkout/bank-transfer</c>.
/// </para>
/// </summary>
public static class CheckoutEndpoints
{
    public static IEndpointRouteBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        // Public on purpose: these are the details every bank-transfer buyer is shown. 404 while
        // unconfigured so the frontend can hide the option instead of showing empty details.
        app.MapGet("/checkout/bank-transfer", (IOptions<BankTransferOptions> options) =>
        {
            var bankTransfer = options.Value;
            return bankTransfer.IsEnabled
                ? Results.Ok(new BankTransferDetailsResponse(
                    bankTransfer.Alias,
                    bankTransfer.AccountHolder,
                    string.IsNullOrWhiteSpace(bankTransfer.Cbu) ? null : bankTransfer.Cbu))
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Bank transfer not available");
        });

        var group = app.MapGroup("/checkout/orders");

        group.MapPost(string.Empty, async (
            CheckoutOrderRequestBody body,
            HttpContext httpContext,
            CreateOrder useCase,
            CancellationToken cancellationToken) =>
        {
            var lines = (body.Items ?? [])
                .Select(item => new CreateOrderLine(item.VariantId, item.Quantity))
                .ToList();

            var paymentMethod = PaymentMethodNames.Parse(body.PaymentMethod);

            var result = await useCase.ExecuteAsync(
                new CreateOrderRequest(ResolveUserId(httpContext.User, body.Email), body.Email, lines, paymentMethod, body.Phone),
                cancellationToken);

            return Results.Created(
                $"/checkout/orders/{result.OrderId}/status",
                new CheckoutOrderResponse(result.OrderId, result.InitPoint));
        }).AddEndpointFilter<OptionalBearerFilter>();

        group.MapGet("/{id:guid}/status", async (Guid id, GetOrderStatus useCase, CancellationToken cancellationToken) =>
        {
            var status = await useCase.ExecuteAsync(id, cancellationToken);
            return status is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(ToStatusResponse(status));
        });

        group.MapPost("/{id:guid}/reconcile", async (
            Guid id,
            ConfirmCheckoutPayment confirmation,
            GetOrderStatus getOrderStatus,
            CancellationToken cancellationToken) =>
        {
            var result = await confirmation.ExecuteAsync(id, cancellationToken);
            if (result.Outcome == ConfirmCheckoutPaymentOutcome.NotFound)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found");
            }

            var status = await getOrderStatus.ExecuteAsync(id, cancellationToken);
            return status is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(ToStatusResponse(status));
        });

        return app;
    }

    private static CheckoutOrderStatusResponse ToStatusResponse(CheckoutStatusResult status) => new(
        status.OrderId,
        status.Status.ToString(),
        PaymentMethodNames.ToWire(status.PaymentMethod),
        status.LastPaymentAttemptStatus,
        status.BuyerEmail,
        status.TotalAmount,
        status.Currency);

    /// <summary>
    /// Never binds <c>UserId</c> from the body (cart-checkout spec "Guest and
    /// Authenticated Checkout"). Returns <see langword="null"/> for an
    /// unauthenticated caller (guest) or an unparseable <c>sub</c>.
    /// <para>
    /// Also returns <see langword="null"/> when an authenticated caller submits an
    /// address other than its own: the order is then a purchase made FOR that
    /// address and behaves exactly like a guest order, so whoever owns the
    /// address claims it on login and is the one able to reveal the keys — which
    /// is also where the delivery email goes (<c>OrderDeliveredHandler</c> sends
    /// to <c>BuyerEmail</c>). Linking it to the payer instead would split the two:
    /// the mail would reach the typed address while only the payer could reveal,
    /// and the recipient would follow a "log in to see your keys" mail to an empty
    /// order list.
    /// </para>
    /// <para>
    /// Consequence, surfaced in the UI before submit: a buyer who changes the
    /// address does not keep that purchase in their own history.
    /// </para>
    /// </summary>
    private static Guid? ResolveUserId(ClaimsPrincipal user, string? buyerEmail)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var tokenEmail = user.FindFirst("email")?.Value;
        if (string.IsNullOrWhiteSpace(tokenEmail)
            || !string.Equals(tokenEmail.Trim(), buyerEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sub = user.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}

/// <summary>One checkout line: variant + quantity only — price and names are always server-resolved.</summary>
public sealed record CheckoutOrderItemRequest(Guid VariantId, int Quantity);

/// <summary>
/// Checkout request body (design section 7 <c>POST /checkout/orders</c>).
/// Deliberately has no <c>UserId</c> member — see <see cref="CheckoutEndpoints"/>.
/// <see cref="PaymentMethod"/> is <c>"mercadopago"</c> (default when omitted) or
/// <c>"bank-transfer"</c>; <see cref="Phone"/> is required for the latter only.
/// </summary>
public sealed record CheckoutOrderRequestBody(
    string Email,
    IReadOnlyList<CheckoutOrderItemRequest> Items,
    string? PaymentMethod = null,
    string? Phone = null);

/// <summary>Durable order reference and the Mercado Pago handoff URL — <see langword="null"/> for a bank-transfer order.</summary>
public sealed record CheckoutOrderResponse(Guid OrderId, string? InitPoint);

/// <summary>Bank account a bank-transfer buyer pays into (<c>GET /checkout/bank-transfer</c>).</summary>
public sealed record BankTransferDetailsResponse(string Alias, string AccountHolder, string? Cbu);

/// <summary>Privacy-safe checkout status for the result-page poller (design section 7).</summary>
public sealed record CheckoutOrderStatusResponse(
    Guid OrderId,
    string Status,
    string PaymentMethod,
    string? LastPaymentAttemptStatus,
    string BuyerEmailMasked,
    decimal TotalAmount,
    string Currency);
