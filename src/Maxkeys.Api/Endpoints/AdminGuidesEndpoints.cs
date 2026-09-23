using Maxkeys.Api.Auth;
using Maxkeys.Application.Guides;

namespace Maxkeys.Api.Endpoints;

/// <summary>Admin activation-guide management endpoints (activation-guides spec). Every route requires the <see cref="AdminPolicy.Name"/> policy.</summary>
public static class AdminGuidesEndpoints
{
    public static IEndpointRouteBuilder MapAdminGuidesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/guides").RequireAuthorization(AdminPolicy.Name);

        group.MapGet(string.Empty, async (ListGuides useCase, CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(cancellationToken)));

        group.MapPost(string.Empty, async (GuideRequest body, CreateGuide useCase, CancellationToken cancellationToken) =>
        {
            var guide = await useCase.ExecuteAsync(body.Slug, body.Title, body.ContentMarkdown, cancellationToken);
            return Results.Created($"/admin/guides/{guide.Id}", guide);
        });

        group.MapPut("/{id:guid}", async (Guid id, GuideRequest body, UpdateGuide useCase, CancellationToken cancellationToken) =>
        {
            var guide = await useCase.ExecuteAsync(id, body.Slug, body.Title, body.ContentMarkdown, cancellationToken);
            return guide is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Guide not found")
                : Results.Ok(guide);
        });

        group.MapDelete("/{id:guid}", async (Guid id, DeleteGuide useCase, CancellationToken cancellationToken) =>
        {
            var deleted = await useCase.ExecuteAsync(id, cancellationToken);
            return deleted
                ? Results.NoContent()
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Guide not found");
        });

        return app;
    }
}

/// <summary>Admin request body for <c>POST /admin/guides</c> and <c>PUT /admin/guides/{id}</c>.</summary>
public sealed record GuideRequest(string Slug, string Title, string? ContentMarkdown);
