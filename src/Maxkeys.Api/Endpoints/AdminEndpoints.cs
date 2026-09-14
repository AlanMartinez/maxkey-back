using System.Security.Claims;
using Maxkeys.Api.Auth;
using Maxkeys.Application.Fulfillment;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Admin-only fulfillment endpoints (design section 7; fulfillment spec:
/// Admin Order Listing, Key Attachment, Admin Authorization). Every route
/// requires the <see cref="AdminPolicy.Name"/> policy.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var meGroup = app.MapGroup("/admin").RequireAuthorization(AdminPolicy.Name);

        // Lets the frontend admin guard confirm the caller passes AdminPolicy without
        // depending on the shape of any other admin endpoint's response (design D5).
        meGroup.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var adminSub = user.FindFirst("sub")?.Value
                ?? throw new InvalidOperationException("Authenticated admin principal is missing a 'sub' claim.");
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

        group.MapPost("/{id:guid}/items/{itemId:guid}/keys", async (
            Guid id,
            Guid itemId,
            AttachKeyRequest body,
            ClaimsPrincipal user,
            AttachKeyToOrderItem useCase,
            CancellationToken cancellationToken) =>
        {
            var adminSub = user.FindFirst("sub")?.Value
                ?? throw new InvalidOperationException("Authenticated admin principal is missing a 'sub' claim.");

            var result = await useCase.ExecuteAsync(id, itemId, body.Code, adminSub, cancellationToken);
            return result is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(new AttachKeyResponse(result.OrderStatus.ToString(), result.Items));
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

/// <summary>One order awaiting fulfillment, with per-item progress, for the admin listing (design section 7).</summary>
public sealed record AdminOrderResponse(
    Guid Id,
    string BuyerEmail,
    string Status,
    DateTimeOffset? PaidAt,
    IReadOnlyList<AdminOrderItemSummary> Items);
