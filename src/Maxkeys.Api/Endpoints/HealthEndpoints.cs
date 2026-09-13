using Maxkeys.Infrastructure.Persistence;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// <c>/health</c> checks Postgres connectivity (design section 12); <c>/health/live</c>
/// checks the process only, so Fly/Railway's liveness probe never fails on a slow
/// or unavailable database — only the readiness surface (<c>/health</c>) does.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

        app.MapGet("/health", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? Results.Ok(new { status = "healthy" })
                : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database unavailable");
        });

        return app;
    }
}
