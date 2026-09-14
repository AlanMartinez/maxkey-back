using Maxkeys.Api.Auth;
using Maxkeys.Application.Buyers;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Admin buyers listing endpoint (admin-buyers spec: Buyer Listing Grouped By
/// Email, Key Exposure in Buyer View, Admin Buyers Authorization; design D4).
/// Requires the <see cref="AdminPolicy.Name"/> policy.
/// </summary>
public static class AdminBuyersEndpoints
{
    public static IEndpointRouteBuilder MapAdminBuyersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/buyers").RequireAuthorization(AdminPolicy.Name);

        group.MapGet(string.Empty, async (
            string? email,
            int? page,
            int? pageSize,
            ListBuyers useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(email, page ?? 1, pageSize ?? 20, cancellationToken);
            return Results.Ok(result);
        });

        return app;
    }
}
