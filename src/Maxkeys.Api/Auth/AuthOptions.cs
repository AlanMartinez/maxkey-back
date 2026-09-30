namespace Maxkeys.Api.Auth;

/// <summary>Signing-key validation mode selected via <c>Auth:Mode</c> (design section 6e/10).</summary>
public enum AuthMode
{
    Jwks,
    Hs256,
}

/// <summary>
/// Binds the <c>Auth</c> configuration section (design section 10, auth spec
/// "Configurable JWT Validation Mode"). Supabase's actual signing mode (JWKS
/// vs. legacy HS256) is confirmed per-project in the Supabase dashboard (task
/// 10.0, pending user confirmation); this option lets either mode work without
/// a code change once confirmed — <see cref="Mode"/> defaults to
/// <see cref="AuthMode.Jwks"/>, the current default for new Supabase projects.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Jwks;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Used only when <see cref="Mode"/> is <see cref="AuthMode.Jwks"/>.</summary>
    public string JwksUrl { get; set; } = string.Empty;

    /// <summary>Used only when <see cref="Mode"/> is <see cref="AuthMode.Hs256"/>. Supabase's legacy shared secret.</summary>
    public string Hs256Secret { get; set; } = string.Empty;

    /// <summary>
    /// Allowlist of <c>sub</c> claim values authorized for admin endpoints (auth
    /// spec "Admin Authorization Policy"). Empty MUST deny every caller.
    /// </summary>
    public string[] AdminSubs { get; set; } = [];

    /// <summary>
    /// Supabase <c>app_metadata.provider</c> values whose tokens carry an email
    /// address the provider itself has verified, and which may therefore be used to
    /// claim a guest order (see <see cref="VerifiedEmailResolver"/>). Defaults to
    /// Google alone — the only login the storefront offers — so the default needs no
    /// entry in <c>fly.toml</c>, whose <c>[env]</c> block is replaced wholesale on
    /// every deploy and has silently dropped keys before. Empty MUST deny every
    /// caller. Adding <c>email</c> here re-opens ownership to unconfirmed
    /// email/password signups and must not be done.
    /// </summary>
    public string[] VerifiedEmailProviders { get; set; } = ["google"];

    /// <summary>
    /// Local-only escape hatch so a developer without a real Supabase admin
    /// account can exercise admin endpoints (e.g. seeding test catalog data)
    /// without logging in. Defaults to <see langword="false"/> and MUST only
    /// ever be set via `dotnet user-secrets` — never in an appsettings*.json
    /// file, so it can't accidentally ship. <see cref="AdminAuthorizationHandler"/>
    /// additionally requires <c>IHostEnvironment.IsDevelopment()</c> before
    /// honoring this flag, so a stray `true` is inert outside local dev even if
    /// it ever leaked into a committed file.
    /// </summary>
    public bool DevBypassAdmin { get; set; }
}
