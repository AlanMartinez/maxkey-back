using Maxkeys.Api.Cors;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Errors;
using Maxkeys.Api.Logging;
using Maxkeys.Infrastructure;
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

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors(Maxkeys.Api.Cors.CorsOptions.PolicyName);

    app.MapHealthEndpoints();
    app.MapCatalogEndpoints();
    app.MapCheckoutEndpoints();

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Exposes the top-level-statement entry point to <c>WebApplicationFactory&lt;Program&gt;</c> in tests.</summary>
public partial class Program;
