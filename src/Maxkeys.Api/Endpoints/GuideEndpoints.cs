using Maxkeys.Application.Guides;

namespace Maxkeys.Api.Endpoints;

/// <summary>Public, unauthenticated guide lookup for the storefront `/article/{slug}` page (activation-guides spec).</summary>
public static class GuideEndpoints
{
    public static IEndpointRouteBuilder MapGuideEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/guides/{slug}", async (string slug, GetGuideBySlug useCase, CancellationToken cancellationToken) =>
        {
            var guide = await useCase.ExecuteAsync(slug, cancellationToken);
            return guide is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Guide not found")
                : Results.Ok(guide);
        });

        return app;
    }
}
