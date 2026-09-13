using System.Collections.Generic;
using System.Net;
using System.Text;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Auth;

/// <summary>
/// Runs the host with <c>Auth:Mode=Jwks</c> and replaces every outbound HTTP
/// call with an in-process fake handler serving <see cref="TestRsaKey"/>'s
/// public key, so JWKS validation is exercised end-to-end (HTTP fetch +
/// <c>JwksKeyCache</c> + JWT bearer pipeline) without a real Supabase project
/// or network access (design section 11 testing strategy). Admin allowlist is
/// deliberately left empty here to also cover "empty allowlist denies
/// everyone" with a genuinely valid token.
/// </summary>
public sealed class JwksApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public const string Issuer = "https://auth.test/auth/v1";
    public const string Audience = "authenticated";

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
                ["Cors:AllowedOrigins:0"] = "http://allowed.test",
                ["Storage:R2PublicBaseUrl"] = "https://img.test",
                ["Auth:Mode"] = "Jwks",
                ["Auth:Issuer"] = Issuer,
                ["Auth:Audience"] = Audience,
                ["Auth:JwksUrl"] = "https://auth.test/auth/v1/.well-known/jwks.json",
            });
        });

        builder.ConfigureServices(services =>
            services.ConfigureHttpClientDefaults(http =>
                http.ConfigurePrimaryHttpMessageHandler(() => new FakeJwksHandler())));
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsyncCore();

    private async Task DisposeAsyncCore()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync().AsTask();
    }
}

/// <summary>Shares one <see cref="JwksApiTestFixture"/> (one Postgres container, one host) across this collection.</summary>
[CollectionDefinition(Name)]
public sealed class JwksApiCollection : ICollectionFixture<JwksApiTestFixture>
{
    public const string Name = "JwksApi";
}

/// <summary>Serves <see cref="TestRsaKey"/>'s public JWKS document for every request, regardless of the requested URL.</summary>
internal sealed class FakeJwksHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(TestRsaKey.JwksJson, Encoding.UTF8, "application/json"),
        });
}
