using System.Security.Claims;
using Maxkeys.Api.Auth;
using Maxkeys.Application.Fulfillment;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Admin vault management endpoints (vault spec). Every route requires the
/// <see cref="AdminPolicy.Name"/> policy, same convention as
/// <see cref="AdminCatalogEndpoints"/>.
/// </summary>
public static class AdminVaultEndpoints
{
    public static IEndpointRouteBuilder MapAdminVaultEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/vault").RequireAuthorization(AdminPolicy.Name);

        group.MapGet("/products", async (ListVaultStock useCase, CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(cancellationToken)));

        group.MapGet("/variants/{id:guid}/keys", async (
            Guid id,
            ListVariantKeys useCase,
            CancellationToken cancellationToken) =>
        {
            var keys = await useCase.ExecuteAsync(id, cancellationToken);
            return keys is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product variant not found")
                : Results.Ok(keys);
        });

        group.MapPost("/variants/{id:guid}/keys", async (
            Guid id,
            LoadVaultKeysRequest body,
            ClaimsPrincipal user,
            LoadVaultKeys useCase,
            CancellationToken cancellationToken) =>
        {
            var adminSub = user.FindFirst("sub")?.Value
                ?? throw new InvalidOperationException("Authenticated admin principal is missing a 'sub' claim.");

            var result = await useCase.ExecuteAsync(id, body.Codes, adminSub, cancellationToken);
            return result is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product variant not found")
                : Results.Ok(result);
        });

        group.MapPut("/products/{id:guid}/toggle", async (
            Guid id,
            ToggleProductVaultRequest body,
            ToggleProductVault useCase,
            CancellationToken cancellationToken) =>
        {
            var enabled = await useCase.ExecuteAsync(id, body.Enabled, cancellationToken);
            return enabled is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found")
                : Results.Ok(new ToggleProductVaultResponse(enabled.Value));
        });

        return app;
    }
}

/// <summary>Admin request body for <c>POST /admin/vault/variants/{id}/keys</c>.</summary>
public sealed record LoadVaultKeysRequest(IReadOnlyList<string> Codes);

/// <summary>Admin request body for <c>PUT /admin/vault/products/{id}/toggle</c>.</summary>
public sealed record ToggleProductVaultRequest(bool Enabled);

/// <summary>Response for <c>PUT /admin/vault/products/{id}/toggle</c>.</summary>
public sealed record ToggleProductVaultResponse(bool VaultEnabled);
