using System.Collections.Generic;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Maxkeys.Api.Tests.Webhooks;

/// <summary>
/// Runs the host with <c>Payments:WebhookEnabled=false</c> (payments-webhook
/// spec "Kill Switch") in its own fixture/collection, mirroring
/// <c>Maxkeys.Api.Tests.Auth.Hs256ApiTestFixture</c>/<c>JwksApiTestFixture</c> —
/// a distinct boolean config value needs a distinct host, since
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> built from an
/// in-memory collection is not mutated mid-test-run.
/// </summary>
public sealed class WebhookDisabledApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

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
                ["Cors:AllowedOrigins:0"] = "http://allowed.test",
                ["Storage:R2PublicBaseUrl"] = "https://img.test",
                ["Payments:AccessToken"] = string.Empty,
                ["Payments:WebhookSecret"] = "unused-secret",
                ["Payments:WebhookEnabled"] = "false",
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

/// <summary>Shares one <see cref="WebhookDisabledApiTestFixture"/> (one Postgres container, one host) across this collection.</summary>
[CollectionDefinition(Name)]
public sealed class WebhookDisabledApiCollection : ICollectionFixture<WebhookDisabledApiTestFixture>
{
    public const string Name = "WebhookDisabledApi";
}
