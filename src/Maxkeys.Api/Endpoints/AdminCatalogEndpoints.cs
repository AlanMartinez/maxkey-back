using Maxkeys.Api.Auth;
using Maxkeys.Application.Catalog;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Admin catalog management endpoints (admin-catalog spec; design D3). Every
/// route requires the <see cref="AdminPolicy.Name"/> policy. <c>PUT</c> takes
/// the full editable record — no <c>PATCH</c>, no separate toggle endpoint;
/// the UI re-sends the record with <c>isActive</c> flipped (design D3).
/// <c>POST /products</c> (<see cref="CreateProduct"/>) and
/// <c>POST /products/{productId}/variants</c> (<see cref="CreateProductVariant"/>)
/// are the admin insert paths outside <c>CatalogSeeder</c>. <c>DELETE /products/{id}</c>
/// is a soft delete (<see cref="DeleteProduct"/>, returns the deactivated record),
/// whereas <c>DELETE /variants/{id}</c> hard-deletes the row (<see cref="DeleteProductVariant"/>).
/// </summary>
public static class AdminCatalogEndpoints
{
    public static IEndpointRouteBuilder MapAdminCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/catalog").RequireAuthorization(AdminPolicy.Name);

        group.MapGet("/products", async (ListAdminProducts useCase, CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(cancellationToken)));

        group.MapPost("/products", async (
            CreateProductRequest body,
            CreateProduct useCase,
            CancellationToken cancellationToken) =>
        {
            var imageKey = ImageKeyPolicy.NormalizeImageKitUploadPath(body.ImageKey);
            var detailImageKey = ImageKeyPolicy.NormalizeImageKitUploadPath(body.DetailImageKey);
            var imageKeys = NormalizeImageKitUploadPaths(body.ImageKeys);
            if (!ImageKeysAreValid(imageKey, detailImageKey, imageKeys))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid image key");
            }

            var product = await useCase.ExecuteAsync(
                body.Slug,
                body.Name,
                body.Platform,
                body.Description,
                imageKey,
                detailImageKey,
                body.IsActive,
                imageKeys ?? [],
                body.ActivationGuide,
                body.ActivationType,
                cancellationToken);
            return Results.Created($"/admin/catalog/products/{product.Id}", product);
        });

        group.MapPut("/products/{id:guid}", async (
            Guid id,
            UpdateProductRequest body,
            UpdateProduct useCase,
            CancellationToken cancellationToken) =>
        {
            var imageKey = ImageKeyPolicy.NormalizeImageKitUploadPath(body.ImageKey);
            var detailImageKey = ImageKeyPolicy.NormalizeImageKitUploadPath(body.DetailImageKey);
            var imageKeys = NormalizeImageKitUploadPaths(body.ImageKeys);
            if (!ImageKeysAreValid(imageKey, detailImageKey, imageKeys))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid image key");
            }

            var product = await useCase.ExecuteAsync(
                id,
                body.Slug,
                body.Name,
                body.Platform,
                body.Description,
                imageKey,
                detailImageKey,
                body.IsActive,
                imageKeys ?? [],
                body.ActivationGuide,
                body.ActivationType,
                cancellationToken);
            return product is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found")
                : Results.Ok(product);
        });

        group.MapDelete("/products/{id:guid}", async (
            Guid id,
            DeleteProduct useCase,
            CancellationToken cancellationToken) =>
        {
            var product = await useCase.ExecuteAsync(id, cancellationToken);
            return product is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found")
                : Results.Ok(product);
        });

        group.MapPost("/products/{productId:guid}/variants", async (
            Guid productId,
            CreateProductVariantRequest body,
            CreateProductVariant useCase,
            CancellationToken cancellationToken) =>
        {
            var variant = await useCase.ExecuteAsync(
                productId, body.Region, body.Edition, body.Price, body.DiscountPercentage, body.Currency, body.SortOrder, body.IsActive, cancellationToken);
            return variant is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found")
                : Results.Created($"/admin/catalog/variants/{variant.Id}", variant);
        });

        group.MapPut("/variants/{id:guid}", async (
            Guid id,
            UpdateProductVariantRequest body,
            UpdateProductVariant useCase,
            CancellationToken cancellationToken) =>
        {
            var variant = await useCase.ExecuteAsync(
                id,
                body.Price,
                body.DiscountPercentage,
                body.Currency,
                body.Region,
                body.Edition,
                body.SortOrder,
                body.IsActive,
                body.IsRecommended,
                cancellationToken);
            return variant is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Variant not found")
                : Results.Ok(variant);
        });

        group.MapDelete("/variants/{id:guid}", async (
            Guid id,
            DeleteProductVariant useCase,
            CancellationToken cancellationToken) =>
        {
            var deleted = await useCase.ExecuteAsync(id, cancellationToken);
            return deleted
                ? Results.NoContent()
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Variant not found");
        });

        return app;
    }

    private static bool ImageKeysAreValid(string? imageKey, string? detailImageKey, IReadOnlyList<string>? imageKeys) =>
        ImageKeyPolicy.IsOptionalKeyValid(imageKey) &&
        ImageKeyPolicy.IsOptionalKeyValid(detailImageKey) &&
        (imageKeys is null || imageKeys.All(ImageKeyPolicy.IsSafeRelativeKey));

    private static IReadOnlyList<string>? NormalizeImageKitUploadPaths(IReadOnlyList<string>? imageKeys) =>
        imageKeys?.Select(ImageKeyPolicy.NormalizeImageKitUploadPath).Cast<string>().ToArray();
}

/// <summary>Admin request body for <c>POST /admin/catalog/products</c>.</summary>
public sealed record CreateProductRequest(
    string Slug,
    string Name,
    string Platform,
    string? Description,
    string? ImageKey,
    string? DetailImageKey,
    bool IsActive,
    IReadOnlyList<string>? ImageKeys,
    string? ActivationGuide,
    string? ActivationType);

/// <summary>Admin request body for <c>PUT /admin/catalog/products/{id}</c> (design D3 contract table).</summary>
public sealed record UpdateProductRequest(
    string Slug,
    string Name,
    string Platform,
    string? Description,
    string? ImageKey,
    string? DetailImageKey,
    bool IsActive,
    IReadOnlyList<string>? ImageKeys,
    string? ActivationGuide,
    string? ActivationType);

/// <summary>Admin request body for <c>POST /admin/catalog/products/{productId}/variants</c> (design D3 contract table).</summary>
public sealed record CreateProductVariantRequest(
    string? Region, string? Edition, decimal Price, decimal? DiscountPercentage, string Currency, int SortOrder, bool IsActive);

/// <summary>
/// Admin request body for <c>PUT /admin/catalog/variants/{id}</c> (design D3 contract
/// table). <see cref="IsRecommended"/> marks the variant shown on the catalog card
/// and preselected on the detail page; <c>true</c> clears the flag on the product's
/// other variants (exclusive per product).
/// </summary>
public sealed record UpdateProductVariantRequest(
    decimal Price,
    decimal? DiscountPercentage,
    string Currency,
    string? Region,
    string? Edition,
    int SortOrder,
    bool IsActive,
    bool IsRecommended);
