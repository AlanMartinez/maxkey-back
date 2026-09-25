using Maxkeys.Application.Carousel;
using Maxkeys.Application.Catalog;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Public, unauthenticated catalog endpoints (design section 7; catalog spec
/// "Product Listing", "Product Detail Lookup").
/// </summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/catalog");

        group.MapGet("/products", async (string? platform, string? q, GetCatalog useCase, CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(platform, q, cancellationToken)));

        group.MapGet("/products/{slug}", async (string slug, HttpRequest request, HttpResponse response, GetProductBySlug useCase, CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(slug, cancellationToken);
            if (result is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found");
            }

            // Weak ETag from Product.UpdatedAt (bumped on every catalog field and by every
            // variant create/update/delete via Product.Touch()) so a 304 never serves a stale
            // price. Cache-Control forces revalidation instead of trusting a local TTL.
            var etag = $"W/\"{result.UpdatedAt.ToUnixTimeMilliseconds():x}\"";
            response.Headers.ETag = etag;
            response.Headers.CacheControl = "public, max-age=0, must-revalidate";

            if (request.Headers.IfNoneMatch == etag)
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return Results.Ok(result.Detail);
        });

        // Public, unauthenticated (carousel spec "Public Carousel Listing"; design D2).
        group.MapGet("/carousel", async (GetCarousel useCase, CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(cancellationToken)));

        return app;
    }
}
