using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Auth;

/// <summary>
/// Decides whether a token's <c>email</c> claim may be used to take ownership of
/// a guest order.
/// <para>
/// Order ownership is resolved by matching this email against
/// <c>Order.BuyerEmail</c> (<c>GetMyOrders</c> claims every matching guest order
/// permanently), and the buyer reveal endpoint is the only path that discloses a
/// key code (ADR-14). That makes this the single control protecting every key in
/// the vault, so it fails closed: the email is returned only when the token was
/// issued through a provider that asserts a verified address
/// (<see cref="AuthOptions.VerifiedEmailProviders"/>, <c>google</c> by default).
/// An email/password signup — which Supabase's public <c>/auth/v1/signup</c>
/// endpoint can mint with an address its owner never confirmed — reports
/// <c>provider: "email"</c> and is refused.
/// </para>
/// <para>
/// A refusal is not an error: the caller still sees every order already linked to
/// its <c>sub</c>, only the claim-by-email step is skipped. Each refusal is logged
/// so a provider added in the Supabase dashboard without updating the allowlist is
/// visible in the logs instead of silently granting access.
/// </para>
/// </summary>
public sealed class VerifiedEmailResolver
{
    private readonly IOptionsMonitor<AuthOptions> _options;
    private readonly ILogger<VerifiedEmailResolver> _logger;

    public VerifiedEmailResolver(IOptionsMonitor<AuthOptions> options, ILogger<VerifiedEmailResolver> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Returns the caller's email when it may be trusted to claim guest orders, or
    /// <see langword="null"/> when it may not.
    /// </summary>
    public string? Resolve(ClaimsPrincipal user)
    {
        var email = user.FindFirst("email")?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var allowedProviders = _options.CurrentValue.VerifiedEmailProviders;
        if (allowedProviders.Length == 0)
        {
            _logger.LogWarning(
                "Auth:VerifiedEmailProviders is empty; refusing to claim guest orders by email for every caller.");
            return null;
        }

        var provider = ReadProvider(user);
        if (provider is null)
        {
            _logger.LogWarning(
                "Token for {Sub} carries no readable app_metadata.provider; refusing to claim guest orders by email.",
                user.FindFirst("sub")?.Value);
            return null;
        }

        if (!allowedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Token for {Sub} was issued by provider {Provider}, which is not in Auth:VerifiedEmailProviders; "
                + "refusing to claim guest orders by email.",
                user.FindFirst("sub")?.Value,
                provider);
            return null;
        }

        return email;
    }

    /// <summary>
    /// Reads <c>app_metadata.provider</c>. Supabase nests it, and the JWT handlers
    /// surface a nested object as a single claim whose value is the raw JSON, so it
    /// needs parsing rather than a lookup by a dotted claim name. Returns
    /// <see langword="null"/> for any shape this cannot read — an unreadable token
    /// must not claim anything.
    /// </summary>
    private static string? ReadProvider(ClaimsPrincipal user)
    {
        var appMetadata = user.FindFirst("app_metadata")?.Value;
        if (string.IsNullOrWhiteSpace(appMetadata))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(appMetadata);
            return document.RootElement.TryGetProperty("provider", out var provider)
                && provider.ValueKind == JsonValueKind.String
                ? provider.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
