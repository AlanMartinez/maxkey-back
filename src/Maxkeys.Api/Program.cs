using Maxkeys.Api.Auth;
using Maxkeys.Api.Cors;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Errors;
using Maxkeys.Api.Logging;
using Maxkeys.Infrastructure;
using Maxkeys.Infrastructure.Persistence;
using Serilog;

SerilogSetup.Bootstrap();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, _, loggerConfiguration) =>
        SerilogSetup.Configure(loggerConfiguration, context.Configuration, context.HostingEnvironment));

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddCorsPolicy(builder.Configuration);
    builder.Services.AddSupabaseJwtAuth(builder.Configuration);

    var app = builder.Build();

    var seedCatalogPath = GetSeedCatalogPath(args);
    if (seedCatalogPath is not null)
    {
        using var seedScope = app.Services.CreateScope();
        var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CatalogSeeder.SeedAsync(db, seedCatalogPath);
        return;
    }

    app.UseExceptionHandler();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors(Maxkeys.Api.Cors.CorsOptions.PolicyName);
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthEndpoints();
    app.MapCatalogEndpoints();
    app.MapCheckoutEndpoints();
    app.MapWebhookEndpoints();
    app.MapMeEndpoints();
    app.MapAdminEndpoints();

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Parses <c>--seed-catalog &lt;path&gt;</c> from the process arguments (ADR-12,
/// task 12.3). Returns <see langword="null"/> when the flag is absent, which
/// keeps the normal host startup path unchanged.
/// </summary>
static string? GetSeedCatalogPath(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--seed-catalog")
        {
            return args[i + 1];
        }
    }

    return null;
}

/// <summary>Exposes the top-level-statement entry point to <c>WebApplicationFactory&lt;Program&gt;</c> in tests.</summary>
public partial class Program;
