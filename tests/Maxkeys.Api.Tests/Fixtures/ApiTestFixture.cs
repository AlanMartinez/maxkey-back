using System.Collections.Generic;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Maxkeys.Api.Tests.Fixtures;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> wired to a real Postgres
/// (reusing <see cref="PostgresFixture"/> — ADR-09) with test-only overrides for
/// the key material and CORS allowlist, so every option validator that runs at
/// startup (<c>KeyCipherOptionsValidator</c> via <c>ValidateOnStart</c>) passes.
/// </summary>
public sealed class ApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    /// <summary>The only origin registered in the test <c>Cors:AllowedOrigins</c> allowlist.</summary>
    public const string AllowedOrigin = "http://allowed.test";

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Keys:EncryptionKey"] = "8WkVdzuEWJDh35lKYZjBWeQhaadFl9ghCp3KRjZYgcY=",
                ["Keys:CurrentVersion"] = "1",
                ["Payments:AccessToken"] = string.Empty,
                // Pin the production gateway selection: appsettings.Development.json enables the fake gateway (docs/local-demo.md).
                ["Payments:Mode"] = string.Empty,
                ["Cors:AllowedOrigins:0"] = AllowedOrigin,
                ["Storage:R2PublicBaseUrl"] = "https://img.test",
            });
        });
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsyncCore();

    private async Task DisposeAsyncCore()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync().AsTask();
    }
}
