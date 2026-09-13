using System.Collections.Generic;
using Maxkeys.Application.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Maxkeys.Api.Tests.DevPayments;

/// <summary>
/// Runs the host in the (default) Development environment with
/// <c>Payments:Mode=Fake</c>, so <c>FakePaymentGateway</c> is resolved as
/// <c>IPaymentGateway</c> and the <c>/dev/payments</c> routes are mapped
/// (docs/local-demo.md). Same test-only key material / CORS overrides as
/// <see cref="Maxkeys.Api.Tests.Fixtures.ApiTestFixture"/> (ADR-09).
/// </summary>
public sealed class FakePaymentsApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public const string InitPointBaseUrl = "http://api.test";
    public const string ReturnUrlTemplate = "http://front.test/checkout/result?orderId={orderId}&status=approved";

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
                ["Payments:Mode"] = "Fake",
                ["Payments:FakeInitPointBaseUrl"] = InitPointBaseUrl,
                ["Payments:FakeReturnUrl"] = ReturnUrlTemplate,
                ["Cors:AllowedOrigins:0"] = "http://allowed.test",
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

/// <summary>Shares one <see cref="FakePaymentsApiTestFixture"/> (one Postgres container, one host) across this collection.</summary>
[CollectionDefinition(Name)]
public sealed class FakePaymentsApiCollection : ICollectionFixture<FakePaymentsApiTestFixture>
{
    public const string Name = "FakePaymentsApi";
}

/// <summary>
/// Same configuration as <see cref="FakePaymentsApiTestFixture"/> but hosted as
/// <c>Production</c>: proves that <c>Payments:Mode=Fake</c> is inert outside
/// Development (no <c>/dev/payments</c> routes, production gateway selection).
/// </summary>
public sealed class FakePaymentsProductionApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Keys:EncryptionKey"] = "8WkVdzuEWJDh35lKYZjBWeQhaadFl9ghCp3KRjZYgcY=",
                ["Keys:CurrentVersion"] = "1",
                ["Payments:AccessToken"] = string.Empty,
                ["Payments:Mode"] = "Fake",
                ["Cors:AllowedOrigins:0"] = "http://allowed.test",
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

/// <summary>Shares one <see cref="FakePaymentsProductionApiTestFixture"/> (one Postgres container, one host) across this collection.</summary>
[CollectionDefinition(Name)]
public sealed class FakePaymentsProductionApiCollection : ICollectionFixture<FakePaymentsProductionApiTestFixture>
{
    public const string Name = "FakePaymentsProductionApi";
}
