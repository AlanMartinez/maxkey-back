using System.Security.Claims;
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
            CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(RequireUserId(user), RequireEmail(user), cancellationToken)));

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

    /// <summary>Supabase JWTs always carry an <c>email</c> claim; <c>JwtSetup.Configure</c> disables inbound claim mapping so it reads back unmapped.</summary>
    private static string RequireEmail(ClaimsPrincipal user)
    {
        var email = user.FindFirst("email")?.Value;
        return string.IsNullOrEmpty(email)
            ? throw new InvalidOperationException("Authenticated principal is missing a valid 'email' claim.")
            : email;
    }
}

/// <summary>Response for <c>POST /me/orders/{id}/items/{itemId}/keys/reveal</c>.</summary>
public sealed record RevealOrderItemKeysResponse(IReadOnlyList<string> Codes);
