using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Maxkeys.Api.Auth;

/// <summary>
/// Registers Supabase JWT bearer authentication (design section 6e; auth spec
/// "Configurable JWT Validation Mode"): <c>Auth:Mode=Jwks</c> validates against
/// Supabase's published signing keys via <see cref="JwksKeyCache"/>;
/// <c>Auth:Mode=Hs256</c> validates against the shared secret (Supabase's
/// legacy signing mode). Also registers the <see cref="AdminPolicy"/> policy.
/// </summary>
public static class JwtSetup
{
    public static IServiceCollection AddSupabaseJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName));
        services.AddHttpClient(nameof(JwksKeyCache));
        services.AddSingleton<JwksKeyCache>();
        services.AddSingleton<IAuthorizationHandler, AdminAuthorizationHandler>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwksKeyCache, IOptions<AuthOptions>>((jwtOptions, jwksKeyCache, authOptions) =>
                Configure(jwtOptions, authOptions.Value, jwksKeyCache));

        services.AddAuthorization(options =>
            options.AddPolicy(AdminPolicy.Name, policy => policy.Requirements.Add(new AdminRequirement())));

        return services;
    }

    /// <summary>
    /// <c>MapInboundClaims = false</c> keeps the token's original <c>"sub"</c>
    /// claim type instead of ASP.NET Core's default remapping to a Microsoft
    /// claim-type URI (design section 6e: "<c>MapInboundClaims=false</c> keeps
    /// 'sub'").
    /// </summary>
    private static void Configure(JwtBearerOptions jwtOptions, AuthOptions authOptions, JwksKeyCache jwksKeyCache)
    {
        jwtOptions.MapInboundClaims = false;
        jwtOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateLifetime = true,
            NameClaimType = "sub",
        };

        if (authOptions.Mode == AuthMode.Hs256)
        {
            jwtOptions.TokenValidationParameters.IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.Hs256Secret));
        }
        else
        {
            jwtOptions.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) =>
                jwksKeyCache.GetKeysForKidAsync(kid).GetAwaiter().GetResult();
        }
    }
}
