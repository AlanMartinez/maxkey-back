using System.Security.Claims;
using Maxkeys.Application.Wishlist;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Authenticated, owner-scoped wishlist endpoints (wishlist spec). Every
/// route requires a valid JWT — same <c>ClaimsPrincipal</c> + <c>sub</c>-claim
/// pattern as <see cref="MeEndpoints"/> (its private helper is duplicated
/// here rather than shared, matching that file's own convention).
/// </summary>
public static class WishlistEndpoints
{
    public static IEndpointRouteBuilder MapWishlistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me/wishlist").RequireAuthorization();

        group.MapGet(string.Empty, async (
            ClaimsPrincipal user,
            GetMyWishlist useCase,
            CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(RequireUserId(user), cancellationToken)));

        group.MapPost(string.Empty, async (
            AddToWishlistRequest body,
            ClaimsPrincipal user,
            AddToWishlist useCase,
            CancellationToken cancellationToken) =>
        {
            var added = await useCase.ExecuteAsync(RequireUserId(user), body.ProductId, cancellationToken);
            return added
                ? Results.NoContent()
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found");
        });

        group.MapDelete("/{productId:guid}", async (
            Guid productId,
            ClaimsPrincipal user,
            RemoveFromWishlist useCase,
            CancellationToken cancellationToken) =>
        {
            await useCase.ExecuteAsync(RequireUserId(user), productId, cancellationToken);
            return Results.NoContent();
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

/// <summary>Request body for <c>POST /me/wishlist</c>.</summary>
public sealed record AddToWishlistRequest(Guid ProductId);
