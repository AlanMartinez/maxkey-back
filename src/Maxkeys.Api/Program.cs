using Maxkeys.Api.Auth;
using Maxkeys.Api.Cors;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Errors;
using Maxkeys.Api.Logging;
using Maxkeys.Application.Security;
using Maxkeys.Infrastructure;
using Maxkeys.Infrastructure.Payments;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

    // --migrate runs Database.MigrateAsync() then exits (ADR-13, task 18a.2).
    // Deployed as the Fly.io release_command / Railway pre-deploy command, never
    // on normal web-instance startup, to avoid a multi-instance migration race.
    // Checked before --seed-catalog so a release step runs --migrate and an
    // operator runs --seed-catalog separately, by hand, whenever the catalog changes.
    if (args.Contains("--migrate"))
    {
        using var migrateScope = app.Services.CreateScope();
        var migrateDb = migrateScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await migrateDb.Database.MigrateAsync();
        return;
    }

    var seedCatalogPath = GetSeedCatalogPath(args);
    if (seedCatalogPath is not null)
    {
        using var seedScope = app.Services.CreateScope();
        var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CatalogSeeder.SeedAsync(db, seedCatalogPath);
        return;
    }

    // --seed-dev loads mock vault stock + buyers for the local demo. Hard-gated to
    // Development so it can never be run against a deployed database by mistake.
    if (args.Contains("--seed-dev"))
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("--seed-dev is only allowed in the Development environment.");
        }

        using var devSeedScope = app.Services.CreateScope();
        var devDb = devSeedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var keyCipher = devSeedScope.ServiceProvider.GetRequiredService<KeyCipher>();
        var seeded = await DevDataSeeder.SeedAsync(devDb, keyCipher);
        Log.Information(seeded ? "Dev data seeded." : "Dev data already present; nothing to do.");
        return;
    }

    app.UseExceptionHandler();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors(Maxkeys.Api.Cors.CorsOptions.PolicyName);
    // Serves wwwroot/ (placeholder product images for the local demo, docs/local-demo.md).
    app.UseStaticFiles();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthEndpoints();
    app.MapCatalogEndpoints();
    app.MapCheckoutEndpoints();
    app.MapWebhookEndpoints();
    app.MapMeEndpoints();
    app.MapAdminEndpoints();
    app.MapAdminCatalogEndpoints();
    app.MapAdminVaultEndpoints();
    app.MapAdminCarouselEndpoints();
    app.MapAdminBuyersEndpoints();

    // Local demo mode (docs/local-demo.md): the fake payment page exists only in
    // Development with Payments:Mode=Fake. Read through IOptionsMonitor so test
    // hosts' configuration overrides are honoured.
    var paymentsOptions = app.Services.GetRequiredService<IOptionsMonitor<MercadoPagoOptions>>().CurrentValue;
    if (app.Environment.IsDevelopment() && paymentsOptions.IsFakeMode)
    {
        app.MapDevPaymentEndpoints();
    }

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
