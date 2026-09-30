using System.Security.Claims;
using Maxkeys.Api.Auth;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Orders;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Authenticated, owner-scoped order history endpoints (design section 7;
/// orders-history spec). Every route requires a valid JWT; ownership is
/// enforced by the use cases, which return <see langword="null"/> for both an
/// unknown order and one owned by someone else, mapped to 404 here (ADR-14).
/// </summary>
public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me/orders").RequireAuthorization();

        group.MapGet(string.Empty, async (
            ClaimsPrincipal user,
            GetMyOrders useCase,
            VerifiedEmailResolver verifiedEmail,
            CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(RequireUserId(user), verifiedEmail.Resolve(user), cancellationToken)));

        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            GetMyOrder useCase,
            CancellationToken cancellationToken) =>
        {
            var order = await useCase.ExecuteAsync(id, RequireUserId(user), cancellationToken);
            return order is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(order);
        });

        group.MapPost("/{id:guid}/items/{itemId:guid}/keys/reveal", async (
            Guid id,
            Guid itemId,
            ClaimsPrincipal user,
            RevealOrderItemKeys useCase,
            CancellationToken cancellationToken) =>
        {
            var codes = await useCase.ExecuteAsync(id, itemId, RequireUserId(user), cancellationToken);
            return codes is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(new RevealOrderItemKeysResponse(codes));
        });

        var notifications = app.MapGroup("/me/notifications").RequireAuthorization();
        notifications.MapGet(string.Empty, async (
            ClaimsPrincipal user,
            ListNotifications useCase,
            CancellationToken cancellationToken) =>
            Results.Ok((await useCase.ExecuteAsync(RequireUserId(user), cancellationToken))
                .Select(notification => new NotificationResponse(
                    notification.Id,
                    notification.Type,
                    notification.OrderId,
                    notification.CreatedAt,
                    notification.ReadAt))));

        notifications.MapPost("/{id:guid}/read", async (
            Guid id,
            ClaimsPrincipal user,
            ReadNotification useCase,
            CancellationToken cancellationToken) =>
        {
            var found = await useCase.ExecuteAsync(id, RequireUserId(user), cancellationToken);
            return found
                ? Results.NoContent()
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Notification not found");
        });

        return app;
    }

    /// <summary>The authorization policy already guarantees an authenticated principal with a valid <c>sub</c> claim.</summary>
    private static Guid RequireUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated principal is missing a valid 'sub' claim.");
    }

}

/// <summary>Response for <c>POST /me/orders/{id}/items/{itemId}/keys/reveal</c>.</summary>
public sealed record RevealOrderItemKeysResponse(IReadOnlyList<string> Codes);

public sealed record NotificationResponse(Guid Id, string Type, Guid OrderId, DateTime CreatedAt, DateTime? ReadAt);
