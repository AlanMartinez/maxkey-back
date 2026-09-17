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
    /// Explicit opt-in that skips <see cref="AdminSubs"/>/JWT validation entirely
    /// for <see cref="Api.Auth.AdminPolicy"/> — local dev convenience so admin
    /// endpoints work without a real Supabase login. Defaults to <see langword="false"/>
    /// (fail-closed); set only in <c>appsettings.Development.json</c>, never in
    /// <c>appsettings.json</c> or a deployed environment's config/secrets. Test
    /// fixtures never set this key, so it stays <see langword="false"/> there even
    /// though the test host also runs as <c>Development</c>.
    /// </summary>
    public bool DevBypassAdmin { get; set; }
}
