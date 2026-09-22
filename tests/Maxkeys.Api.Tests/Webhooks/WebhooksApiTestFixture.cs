using System.Collections.Generic;
using System.Net;
using System.Text;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Infrastructure.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Webhooks;

/// <summary>
/// Runs the host with a non-empty <c>Payments:AccessToken</c> so
/// <see cref="MercadoPagoGateway"/> is
/// resolved as <c>IPaymentGateway</c> (design section 3/9), then replaces its typed
/// <c>HttpClient</c>'s primary handler with <see cref="FakeMercadoPagoHandler"/>
/// — the same in-process fake-handler pattern as
/// <c>Maxkeys.Api.Tests.Auth.JwksApiTestFixture</c> — so <c>GET
/// /v1/payments/{id}</c> is exercised end-to-end without a real Mercado Pago
/// sandbox. <c>Payments:WebhookEnabled=true</c> here; see
/// <see cref="WebhookDisabledApiTestFixture"/> for the kill-switch scenario.
/// </summary>
public sealed class WebhooksApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public const string WebhookSecret = "test-mp-webhook-secret";

    public FakeMercadoPagoHandler Handler { get; } = new();

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
                ["Payments:AccessToken"] = "test-mp-access-token",
                // Pin the production gateway selection: appsettings.Development.json enables the fake gateway (docs/local-demo.md).
                ["Payments:Mode"] = string.Empty,
                ["Payments:WebhookSecret"] = WebhookSecret,
                ["Payments:WebhookEnabled"] = "true",
            });
        });

        builder.ConfigureServices(services =>
            services.AddHttpClient(nameof(MercadoPagoGateway))
                .ConfigurePrimaryHttpMessageHandler(() => Handler));
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsyncCore();

    private async Task DisposeAsyncCore()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync().AsTask();
    }
}

/// <summary>Shares one <see cref="WebhooksApiTestFixture"/> (one Postgres container, one host) across this collection.</summary>
[CollectionDefinition(Name)]
public sealed class WebhooksApiCollection : ICollectionFixture<WebhooksApiTestFixture>
{
    public const string Name = "WebhooksApi";
}

/// <summary>
/// Serves a configurable <c>GET /v1/payments/{id}</c> response per payment id, set up
/// per test via <see cref="SetPaymentResponse"/>; any other request 404s.
/// </summary>
public sealed class FakeMercadoPagoHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _paymentResponses = new();
    private readonly Dictionary<string, string> _searchResponses = new();

    public void SetPaymentResponse(string paymentId, string externalReference, decimal amount, string currency, string status)
    {
        _paymentResponses[paymentId] =
            $$"""
            {"id":{{paymentId}},"status":"{{status}}","external_reference":"{{externalReference}}","transaction_amount":{{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"currency_id":"{{currency}}"}
            """;
    }

    public void SetApprovedPaymentSearchResponse(string externalReference, string paymentId, decimal amount, string currency)
    {
        _searchResponses[externalReference] =
            $$"""
            {"results":[{"id":{{paymentId}},"status":"approved","external_reference":"{{externalReference}}","transaction_amount":{{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"currency_id":"{{currency}}"}]}
            """;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        const string paymentsPrefix = "/v1/payments/";
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (request.Method == HttpMethod.Get && path == "/v1/payments/search")
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri?.Query ?? string.Empty);
            var externalReference = query.TryGetValue("external_reference", out var values) ? values.ToString() : null;
            if (externalReference is not null && _searchResponses.TryGetValue(externalReference, out var searchJson))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(searchJson, Encoding.UTF8, "application/json"),
                });
            }
        }

        if (request.Method == HttpMethod.Get && path.StartsWith(paymentsPrefix, StringComparison.Ordinal))
        {
            var paymentId = path[paymentsPrefix.Length..];
            if (_paymentResponses.TryGetValue(paymentId, out var json))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
