using System.Security.Claims;
using Maxkeys.Api.Auth;
using Maxkeys.Application.Buyers;
using Maxkeys.Application.Fulfillment;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Admin-only fulfillment endpoints (design section 7; fulfillment spec:
/// Admin Order Listing, Key Attachment, Admin Authorization; admin-buyers
/// spec: Order Detail via <c>GET /admin/orders/{id}</c>). Every route
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

        // "status" is accepted for API-contract compatibility (design §7); the MVP has exactly
        // one admin queue (AwaitingFulfillment), so the parameter is currently unused for filtering.
        group.MapGet(string.Empty, async (string? status, ListOrdersAwaitingFulfillment useCase, CancellationToken cancellationToken) =>
        {
            var orders = await useCase.ExecuteAsync(cancellationToken);
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

    private static AdminOrderResponse ToResponse(AdminOrderSummary order) =>
        new(order.Id, order.BuyerEmail, order.Status.ToString(), order.PaidAt, order.Items);
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

/// <summary>One order awaiting fulfillment, with per-item progress, for the admin listing (design section 7).</summary>
public sealed record AdminOrderResponse(
    Guid Id,
    string BuyerEmail,
    string Status,
    DateTimeOffset? PaidAt,
    IReadOnlyList<AdminOrderItemSummary> Items);
