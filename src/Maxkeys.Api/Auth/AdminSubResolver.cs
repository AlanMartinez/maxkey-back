using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Auth;

/// <summary>
/// Resolves the admin identity an endpoint records as <c>loadedBy</c> /
/// <c>deliveredBy</c>. Normally the JWT <c>sub</c> claim. When
/// <see cref="AuthOptions.DevBypassAdmin"/> lets an anonymous caller through
/// (same double condition as <see cref="AdminAuthorizationHandler"/>), there is
/// no principal to read from, so a fixed placeholder is returned instead —
/// otherwise every sub-dependent admin endpoint answered the bypassed caller
/// with a 500 while the ones that never touch <c>sub</c> worked.
/// </summary>
public sealed class AdminSubResolver
{
    public const string DevBypassSub = "dev-bypass-admin";

    private readonly IOptionsMonitor<AuthOptions> _options;
    private readonly IHostEnvironment _environment;

    public AdminSubResolver(IOptionsMonitor<AuthOptions> options, IHostEnvironment environment)
    {
        _options = options;
        _environment = environment;
    }

    public string Resolve(ClaimsPrincipal user)
    {
        var sub = user.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(sub))
        {
            return sub;
        }

        if (_environment.IsDevelopment() && _options.CurrentValue.DevBypassAdmin)
        {
            return DevBypassSub;
        }

        throw new InvalidOperationException("Authenticated admin principal is missing a 'sub' claim.");
    }
}
