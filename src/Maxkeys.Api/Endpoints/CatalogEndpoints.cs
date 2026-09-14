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

        group.MapGet("/products/{slug}", async (string slug, GetProductBySlug useCase, CancellationToken cancellationToken) =>
        {
            var product = await useCase.ExecuteAsync(slug, cancellationToken);
            return product is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found")
                : Results.Ok(product);
        });

        // Public, unauthenticated (carousel spec "Public Carousel Listing"; design D2).
        group.MapGet("/carousel", async (GetCarousel useCase, CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(cancellationToken)));

        return app;
    }
}
