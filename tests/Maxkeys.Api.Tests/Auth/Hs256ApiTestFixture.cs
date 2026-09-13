using System.Collections.Generic;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Maxkeys.Api.Tests.Auth;

/// <summary>
/// Runs the host with <c>Auth:Mode=Hs256</c> so tests can mint tokens locally
/// with <see cref="Hs256Secret"/> (auth spec "Configurable JWT Validation
/// Mode"). Shares the same test-only key material / CORS overrides as
/// <see cref="Maxkeys.Api.Tests.Fixtures.ApiTestFixture"/> (ADR-09).
/// </summary>
public sealed class Hs256ApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public const string Hs256Secret = "test-hs256-secret-at-least-32-bytes-long!!";
    public const string Issuer = "https://auth.test/auth/v1";
    public const string Audience = "authenticated";
    public const string AdminSub = "11111111-1111-1111-1111-111111111111";

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
                ["Cors:AllowedOrigins:0"] = "http://allowed.test",
                ["Storage:R2PublicBaseUrl"] = "https://img.test",
                ["Auth:Mode"] = "Hs256",
                ["Auth:Issuer"] = Issuer,
                ["Auth:Audience"] = Audience,
                ["Auth:Hs256Secret"] = Hs256Secret,
                ["Auth:AdminSubs:0"] = AdminSub,
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

/// <summary>Shares one <see cref="Hs256ApiTestFixture"/> (one Postgres container, one host) across this collection.</summary>
[CollectionDefinition(Name)]
public sealed class Hs256ApiCollection : ICollectionFixture<Hs256ApiTestFixture>
{
    public const string Name = "Hs256Api";
}
