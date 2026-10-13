using System.Security.Claims;
using Maxkeys.Api.Auth;
using Maxkeys.Application.Buyers;
using Maxkeys.Application.Checkout;
using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Payments;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Admin-only fulfillment endpoints (design section 7; fulfillment spec:
/// Admin Order Listing, Key Attachment, Admin Authorization; admin-buyers
/// spec: Order Detail via <c>GET /admin/orders/{id}</c>), plus the bank-transfer
/// validation queue (<c>?status=awaiting-transfer</c>, approve/reject). Every route
/// requires the <see cref="AdminPolicy.Name"/> policy.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var meGroup = app.MapGroup("/admin").RequireAuthorization(AdminPolicy.Name);

        // Lets the frontend admin guard confirm the caller passes AdminPolicy without
        // depending on the shape of any other admin endpoint's response (design D5).
        meGroup.MapGet("/me", (ClaimsPrincipal user, AdminSubResolver adminSubs) =>
        {
            var adminSub = adminSubs.Resolve(user);
            return Results.Ok(new AdminMeResponse(adminSub));
        });

        var group = app.MapGroup("/admin/orders").RequireAuthorization(AdminPolicy.Name);

        // "status=awaiting-transfer" selects the bank-transfer validation queue; any other value
        // (or none) keeps the original single fulfillment queue (AwaitingFulfillment), as before.
        group.MapGet(string.Empty, async (
            string? status,
            ListOrdersAwaitingFulfillment awaitingFulfillment,
            ListOrdersAwaitingTransferValidation awaitingTransfer,
            CancellationToken cancellationToken) =>
        {
            var orders = string.Equals(status, AwaitingTransferStatusFilter, StringComparison.OrdinalIgnoreCase)
                ? await awaitingTransfer.ExecuteAsync(cancellationToken)
                : await awaitingFulfillment.ExecuteAsync(cancellationToken);
            return Results.Ok(orders.Select(ToResponse).ToList());
        });

        // Full order detail for the admin buyers modal — key ids/status only, never a
        // key code (admin-buyers spec: Order Detail, Key Exposure in Buyer View).
        group.MapGet("/{id:guid}", async (
            Guid id,
            GetAdminOrderDetail useCase,
            CancellationToken cancellationToken) =>
        {
            var detail = await useCase.ExecuteAsync(id, cancellationToken);
            return detail is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(detail);
        });

        // Same OrderApproved outbox event as an approved Mercado Pago payment, so the order
        // continues through vault auto-assignment and the operator email unchanged.
        group.MapPost("/{id:guid}/approve-transfer", async (
            Guid id,
            ClaimsPrincipal user,
            AdminSubResolver adminSubs,
            ApproveBankTransfer useCase,
            CancellationToken cancellationToken) =>
        {
            var order = await useCase.ExecuteAsync(id, adminSubs.Resolve(user), cancellationToken);
            return order is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(ToResponse(order));
        });

        group.MapPost("/{id:guid}/reject-transfer", async (
            Guid id,
            ClaimsPrincipal user,
            AdminSubResolver adminSubs,
            RejectBankTransfer useCase,
            CancellationToken cancellationToken) =>
        {
            var order = await useCase.ExecuteAsync(id, adminSubs.Resolve(user), cancellationToken);
            return order is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(ToResponse(order));
        });

        group.MapPost("/{id:guid}/items/{itemId:guid}/keys", async (
            Guid id,
            Guid itemId,
            AttachKeyRequest body,
            ClaimsPrincipal user,
            AdminSubResolver adminSubs,
            AttachKeyToOrderItem useCase,
            CancellationToken cancellationToken) =>
        {
            var adminSub = adminSubs.Resolve(user);

            var result = await useCase.ExecuteAsync(id, itemId, body.Code, adminSub, cancellationToken);
            return result is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(new AttachKeyResponse(result.OrderStatus.ToString(), result.Items));
        });

        group.MapPost("/{id:guid}/resend-delivery", async (
            Guid id,
            ClaimsPrincipal user,
            AdminSubResolver adminSubs,
            RequestDeliveryResend useCase,
            CancellationToken cancellationToken) =>
        {
            var adminSub = adminSubs.Resolve(user);

            var outboxEventId = await useCase.ExecuteAsync(id, adminSub, cancellationToken);
            return outboxEventId is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Accepted(value: new ResendDeliveryResponse(outboxEventId.Value));
        });

        group.MapPost("/{id:guid}/assign-keys", async (
            Guid id,
            AssignVaultKeysToOrder useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(id, cancellationToken);
            return result is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(new AssignKeysResponse(result.OrderStatus.ToString(), result.AllItemsComplete, result.Items));
        });

        group.MapPost("/{id:guid}/deliver", async (
            Guid id,
            ClaimsPrincipal user,
            AdminSubResolver adminSubs,
            DeliverOrder useCase,
            CancellationToken cancellationToken) =>
        {
            var adminSub = adminSubs.Resolve(user);

            var status = await useCase.ExecuteAsync(id, adminSub, cancellationToken);
            return status is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(new DeliverOrderResponse(status.Value.ToString()));
        });

        return app;
    }

    private const string AwaitingTransferStatusFilter = "awaiting-transfer";

    private static AdminOrderResponse ToResponse(AdminOrderSummary order) => new(
        order.Id,
        order.BuyerEmail,
        order.BuyerPhone,
        order.Status.ToString(),
        PaymentMethodNames.ToWire(order.PaymentMethod),
        order.CreatedAt,
        order.TotalAmount,
        order.Currency,
        order.PaidAt,
        order.Items);
}

/// <summary>Response for <c>GET /admin/me</c> — confirms the caller passes <see cref="AdminPolicy"/> (design D5).</summary>
public sealed record AdminMeResponse(string Sub);

/// <summary>Admin request body for attaching one key code (masked by <c>SensitiveDataPolicy</c>).</summary>
public sealed record AttachKeyRequest(string Code);

/// <summary>Order status and per-item progress after an attach (design section 7).</summary>
public sealed record AttachKeyResponse(string OrderStatus, IReadOnlyList<AttachKeyToOrderItemResultItem> Items);

/// <summary>Acknowledges a queued delivery-email resend (admin-buyers spec: Resend Delivery Email; design D1).</summary>
public sealed record ResendDeliveryResponse(Guid OutboxEventId);

/// <summary>
/// Response for <c>POST /admin/orders/{id}/assign-keys</c>. Status is the enum name, like every
/// other order response here — the bare use-case result would serialize it as an integer.
/// </summary>
public sealed record AssignKeysResponse(string OrderStatus, bool AllItemsComplete, IReadOnlyList<AssignVaultKeysToOrderResultItem> Items);

/// <summary>Response for <c>POST /admin/orders/{id}/deliver</c>.</summary>
public sealed record DeliverOrderResponse(string Status);

/// <summary>One order in an admin queue, with per-item progress, for the admin listing (design section 7) and the transfer approve/reject responses.</summary>
public sealed record AdminOrderResponse(
    Guid Id,
    string BuyerEmail,
    string? BuyerPhone,
    string Status,
    string PaymentMethod,
    DateTimeOffset CreatedAt,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset? PaidAt,
    IReadOnlyList<AdminOrderItemSummary> Items);
