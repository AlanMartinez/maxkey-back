using Serilog;
using Serilog.Formatting.Compact;

namespace Maxkeys.Api.Logging;

/// <summary>
/// Serilog configuration (design section 12): console sink, compact rendered
/// text in Development, <see cref="CompactJsonFormatter"/> JSON in Production
/// (Fly/Railway aggregate stdout). <see cref="Enrichers"/> add correlation via
/// <see cref="CorrelationIdMiddleware"/> and machine name.
/// </summary>
public static class SerilogSetup
{
    /// <summary>Minimal bootstrap logger used before the host (and its configuration) is available.</summary>
    public static void Bootstrap()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();
    }

    /// <summary>Full configuration, applied once the host's <see cref="IConfiguration"/> and <see cref="IHostEnvironment"/> are available.</summary>
    public static void Configure(LoggerConfiguration loggerConfiguration, IConfiguration configuration, IHostEnvironment environment)
    {
        loggerConfiguration
            .ReadFrom.Configuration(configuration)
            .Destructure.With<SensitiveDataPolicy>()
            .Enrich.FromLogContext()
            .Enrich.WithMachineName();

        if (environment.IsDevelopment())
        {
            loggerConfiguration.WriteTo.Console();
        }
        else
        {
            loggerConfiguration.WriteTo.Console(new CompactJsonFormatter());
        }
    }
}
