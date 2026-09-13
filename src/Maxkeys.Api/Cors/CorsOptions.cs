namespace Maxkeys.Api.Cors;

/// <summary>
/// Binds the <c>Cors</c> configuration section (design section 10/12; ADR-18
/// consequence — the frontend is a separate origin so CORS is mandatory, not a
/// same-origin convenience). No credentials mode (bearer header only).
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "Default";

    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>
/// Registers the CORS policy from <see cref="CorsOptions.AllowedOrigins"/>. An
/// empty allowlist yields a policy that allows no origin — a wildcard origin is
/// never registered, even when the configuration is left empty (design §10/§12).
/// </summary>
public static class CorsServiceCollectionExtensions
{
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            // Read lazily: the CORS options factory runs after the host is fully built
            // (test hosts append configuration overrides right before that point), so
            // reading `configuration` here — instead of capturing a value eagerly at
            // registration time — always sees the final, merged configuration.
            var allowedOrigins = configuration
                .GetSection(CorsOptions.SectionName)
                .Get<CorsOptions>()?.AllowedOrigins ?? [];

            options.AddPolicy(CorsOptions.PolicyName, policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
                }
            });
        });

        return services;
    }
}
