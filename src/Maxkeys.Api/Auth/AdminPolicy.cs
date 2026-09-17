using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Auth;

/// <summary>
/// Authorization policy name for admin endpoints (auth spec "Admin
/// Authorization Policy"; fulfillment spec "Admin Authorization"). An
/// authenticated caller's <c>sub</c> claim must be present in
/// <see cref="AuthOptions.AdminSubs"/>. An empty allowlist denies every
/// caller, including a valid Supabase user — fail-closed by design.
/// </summary>
public static class AdminPolicy
{
    public const string Name = "Admin";
}

public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Succeeds only when the caller's <c>sub</c> claim is present in the
/// currently configured <see cref="AuthOptions.AdminSubs"/> allowlist. Never
/// succeeds for an empty allowlist (auth spec "Empty allowlist denies
/// everyone") — except the explicit <see cref="AuthOptions.DevBypassAdmin"/>
/// opt-in, which succeeds unconditionally, including for a request with no
/// <c>Authorization</c> header at all (the requirement still runs against an
/// unauthenticated principal; calling <see cref="AuthorizationHandlerContext.Succeed"/>
/// here turns what would otherwise be a 401 challenge into success).
/// </summary>
public sealed class AdminAuthorizationHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly IOptionsMonitor<AuthOptions> _options;

    public AdminAuthorizationHandler(IOptionsMonitor<AuthOptions> options)
    {
        _options = options;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (_options.CurrentValue.DevBypassAdmin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var sub = context.User.FindFirst("sub")?.Value;
        var adminSubs = _options.CurrentValue.AdminSubs;

        if (!string.IsNullOrEmpty(sub) && adminSubs.Contains(sub, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
