using System.Net;
using System.Net.Http.Json;
using Maxkeys.Api.Auth;
using Maxkeys.Api.Endpoints;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>
/// Covers <see cref="Maxkeys.Api.Auth.AuthOptions.DevBypassAdmin"/>: a local-only escape hatch
/// that lets a developer without a real Supabase admin account exercise admin endpoints (e.g.
/// seeding test catalog data). Verifies both that it works when enabled in Development, and that
/// it stays inert outside Development even if the flag is somehow set — the "NUNCA EN PROD"
/// requirement that motivated the double condition in <c>AdminAuthorizationHandler</c>.
/// </summary>
public sealed class DevBypassAdminTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public Task InitializeAsync() => _postgres.InitializeAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync();

    private WebApplicationFactory<Program> BuildFactory(string environment, bool devBypassAdmin)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                    ["Keys:EncryptionKey"] = "8WkVdzuEWJDh35lKYZjBWeQhaadFl9ghCp3KRjZYgcY=",
                    ["Keys:CurrentVersion"] = "1",
                    ["Payments:AccessToken"] = string.Empty,
                    ["Payments:Mode"] = string.Empty,
                    ["Cors:AllowedOrigins:0"] = "http://allowed.test",
                    ["Storage:R2PublicBaseUrl"] = "https://img.test",
                    ["Auth:Mode"] = "Hs256",
                    ["Auth:Issuer"] = "https://auth.test/auth/v1",
                    ["Auth:Audience"] = "authenticated",
                    ["Auth:Hs256Secret"] = "test-hs256-secret-at-least-32-bytes-long!!",
                    ["Auth:DevBypassAdmin"] = devBypassAdmin ? "true" : "false",
                });
            });
        });
    }

    [Fact]
    public async Task Anonymous_request_succeeds_when_bypass_enabled_in_development()
    {
        using var factory = BuildFactory(Environments.Development, devBypassAdmin: true);
        var response = await factory.CreateClient().GetAsync("/admin/catalog/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Bypassed_caller_gets_a_placeholder_sub_instead_of_a_500()
    {
        // Sub-dependent endpoints (attach key, deliver, vault load, /me) used to throw on the
        // anonymous principal the bypass lets through; the resolver hands them a fixed identity.
        using var factory = BuildFactory(Environments.Development, devBypassAdmin: true);
        var response = await factory.CreateClient().GetAsync("/admin/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AdminMeResponse>();
        Assert.Equal(AdminSubResolver.DevBypassSub, body!.Sub);
    }

    [Fact]
    public async Task Bypass_flag_is_ignored_outside_development()
    {
        using var factory = BuildFactory(Environments.Production, devBypassAdmin: true);
        var response = await factory.CreateClient().GetAsync("/admin/catalog/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
