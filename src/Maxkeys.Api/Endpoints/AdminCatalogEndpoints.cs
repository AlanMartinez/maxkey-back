using Maxkeys.Api.Auth;
using Maxkeys.Application.Catalog;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Admin catalog management endpoints (admin-catalog spec; design D3). Every
/// route requires the <see cref="AdminPolicy.Name"/> policy. <c>PUT</c> takes
/// the full editable record — no <c>PATCH</c>, no separate toggle endpoint;
/// the UI re-sends the record with <c>isActive</c> flipped (design D3).
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
            var product = await useCase.ExecuteAsync(
                body.Slug, body.Name, body.Platform, body.Description, body.ImageKey, body.IsActive, cancellationToken);
            return product is null
                ? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Product slug already exists")
                : Results.Created($"/admin/catalog/products/{product.Id}", product);
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

        group.MapDelete("/variants/{id:guid}", async (
            Guid id,
            DeleteProductVariant useCase,
            CancellationToken cancellationToken) =>
        {
            var variant = await useCase.ExecuteAsync(id, cancellationToken);
            return variant is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Variant not found")
                : Results.Ok(variant);
        });

        group.MapPut("/products/{id:guid}", async (
            Guid id,
            UpdateProductRequest body,
            UpdateProduct useCase,
            CancellationToken cancellationToken) =>
        {
            var product = await useCase.ExecuteAsync(
                id, body.Name, body.Platform, body.Description, body.ImageKey, body.IsActive, cancellationToken);
            return product is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found")
                : Results.Ok(product);
        });

        group.MapPut("/variants/{id:guid}", async (
            Guid id,
            UpdateProductVariantRequest body,
            UpdateProductVariant useCase,
            CancellationToken cancellationToken) =>
        {
            var variant = await useCase.ExecuteAsync(
                id, body.Price, body.DiscountPercentage, body.Currency, body.Region, body.Edition, body.SortOrder, body.IsActive, cancellationToken);
            return variant is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Variant not found")
                : Results.Ok(variant);
        });

        return app;
    }
}

/// <summary>Admin request body for <c>POST /admin/catalog/products</c> (design D3 contract table).</summary>
public sealed record CreateProductRequest(string Slug, string Name, string Platform, string? Description, string? ImageKey, bool IsActive);

/// <summary>Admin request body for <c>PUT /admin/catalog/products/{id}</c> (design D3 contract table).</summary>
public sealed record UpdateProductRequest(string Name, string Platform, string? Description, string? ImageKey, bool IsActive);

/// <summary>Admin request body for <c>POST /admin/catalog/products/{productId}/variants</c> (design D3 contract table).</summary>
public sealed record CreateProductVariantRequest(
    string? Region, string? Edition, decimal Price, decimal? DiscountPercentage, string Currency, int SortOrder, bool IsActive);

/// <summary>Admin request body for <c>PUT /admin/catalog/variants/{id}</c> (design D3 contract table).</summary>
public sealed record UpdateProductVariantRequest(
    decimal Price, decimal? DiscountPercentage, string Currency, string? Region, string? Edition, int SortOrder, bool IsActive);
