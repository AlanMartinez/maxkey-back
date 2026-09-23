namespace Maxkeys.Application.Guides;

/// <summary>
/// An activation guide (activation-guides spec). One shape serves the admin list/create/update
/// responses and the public `GET /guides/{slug}` response — there is no draft/published split
/// that would otherwise give the two views different fields.
/// </summary>
public sealed record GuideDto(Guid Id, string Slug, string Title, string ContentMarkdown);
