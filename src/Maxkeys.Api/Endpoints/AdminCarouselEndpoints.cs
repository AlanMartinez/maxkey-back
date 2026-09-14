using Maxkeys.Api.Auth;
using Maxkeys.Application.Carousel;

namespace Maxkeys.Api.Endpoints;

/// <summary>
/// Admin carousel management endpoints (carousel spec; design D2 contract table).
/// Every route requires the <see cref="AdminPolicy.Name"/> policy. <c>POST</c>/<c>PUT</c>
/// share <see cref="CarouselSlideRequest"/> — the same body shape the admin UI re-sends
/// on every edit, including reorders and toggles.
/// </summary>
public static class AdminCarouselEndpoints
{
    public static IEndpointRouteBuilder MapAdminCarouselEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/carousel").RequireAuthorization(AdminPolicy.Name);

        group.MapGet(string.Empty, async (ListCarouselSlides useCase, CancellationToken cancellationToken) =>
            Results.Ok(await useCase.ExecuteAsync(cancellationToken)));

        group.MapPost(string.Empty, async (
            CarouselSlideRequest body,
            CreateCarouselSlide useCase,
            CancellationToken cancellationToken) =>
        {
            var slide = await useCase.ExecuteAsync(
                body.ProductId, body.SortOrder, body.IsActive, body.Title, body.Caption, body.ImageKey, cancellationToken);
            return Results.Created($"/admin/carousel/{slide.Id}", slide);
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            CarouselSlideRequest body,
            UpdateCarouselSlide useCase,
            CancellationToken cancellationToken) =>
        {
            var slide = await useCase.ExecuteAsync(
                id, body.ProductId, body.SortOrder, body.IsActive, body.Title, body.Caption, body.ImageKey, cancellationToken);
            return slide is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Carousel slide not found")
                : Results.Ok(slide);
        });

        group.MapDelete("/{id:guid}", async (Guid id, DeleteCarouselSlide useCase, CancellationToken cancellationToken) =>
        {
            var deleted = await useCase.ExecuteAsync(id, cancellationToken);
            return deleted
                ? Results.NoContent()
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Carousel slide not found");
        });

        return app;
    }
}

/// <summary>Admin request body for <c>POST /admin/carousel</c> and <c>PUT /admin/carousel/{id}</c> (design contract table).</summary>
public sealed record CarouselSlideRequest(
    Guid ProductId, int SortOrder, bool IsActive, string? Title, string? Caption, string? ImageKey);
