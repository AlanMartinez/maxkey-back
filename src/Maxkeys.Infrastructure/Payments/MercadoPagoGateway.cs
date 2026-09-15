using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxkeys.Application.Payments;
using Maxkeys.Domain.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxkeys.Infrastructure.Payments;

/// <summary>
/// Mercado Pago Checkout Pro gateway (ADR-10): a typed <see cref="HttpClient"/>
/// calling only <c>POST /checkout/preferences</c> and <c>GET /v1/payments/{id}</c>
/// directly, instead of the official SDK, whose surface two endpoints do not
/// justify. Registered as <see cref="IPaymentGateway"/> when
/// <c>Payments:AccessToken</c> is non-empty (design section 3/9); the typed
/// client is configured with the base address and bearer token in
/// <c>DependencyInjection.AddPaymentGateway</c>.
/// </summary>
public sealed class MercadoPagoGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<MercadoPagoOptions> _options;
    private readonly IOptionsMonitor<FrontendOptions> _frontendOptions;
    private readonly ILogger<MercadoPagoGateway> _logger;

    public MercadoPagoGateway(
        HttpClient httpClient,
        IOptionsMonitor<MercadoPagoOptions> options,
        IOptionsMonitor<FrontendOptions> frontendOptions,
        ILogger<MercadoPagoGateway> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _frontendOptions = frontendOptions;
        _logger = logger;
    }

    public async Task<PaymentPreference> CreatePreferenceAsync(Order order, CancellationToken cancellationToken = default)
    {
        var notificationUrl = _options.CurrentValue.NotificationUrl;
        var backUrls = BuildBackUrls(_frontendOptions.CurrentValue.BaseUrl);
        var request = new MercadoPagoPreferenceRequest(
            order.Id.ToString(),
            order.Items
                .Select(item => new MercadoPagoPreferenceItem(
                    $"{item.ProductNameSnapshot} - {item.VariantNameSnapshot}", item.Quantity, item.UnitPrice, order.Currency))
                .ToList(),
            string.IsNullOrWhiteSpace(notificationUrl) ? null : notificationUrl,
            backUrls,
            backUrls is null ? null : "approved");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/checkout/preferences", request, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("Failed to reach Mercado Pago while creating a preference.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Mercado Pago preference creation failed with status {StatusCode}.", response.StatusCode);
            throw new PaymentGatewayException($"Mercado Pago preference creation failed with status {(int)response.StatusCode}.");
        }

        var body = await response.Content.ReadFromJsonAsync<MercadoPagoPreferenceResponse>(JsonOptions, cancellationToken)
            ?? throw new PaymentGatewayException("Mercado Pago returned an empty preference response.");

        return new PaymentPreference(body.Id, body.InitPoint);
    }

    public async Task<PaymentInfo> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync($"/v1/payments/{paymentId}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("Failed to reach Mercado Pago while fetching a payment.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Mercado Pago payment fetch failed with status {StatusCode}.", response.StatusCode);
            throw new PaymentGatewayException($"Mercado Pago payment fetch failed with status {(int)response.StatusCode}.");
        }

        var body = await response.Content.ReadFromJsonAsync<MercadoPagoPaymentResponse>(JsonOptions, cancellationToken)
            ?? throw new PaymentGatewayException("Mercado Pago returned an empty payment response.");

        return new PaymentInfo(body.Id.ToString(), body.Status, body.ExternalReference, body.TransactionAmount, body.CurrencyId);
    }

    public async Task<PaymentInfo?> FindApprovedPaymentAsync(string externalReference, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(
                $"/v1/payments/search?external_reference={Uri.EscapeDataString(externalReference)}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("Failed to reach Mercado Pago while searching payments.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Mercado Pago payment search failed with status {StatusCode}.", response.StatusCode);
            throw new PaymentGatewayException($"Mercado Pago payment search failed with status {(int)response.StatusCode}.");
        }

        var body = await response.Content.ReadFromJsonAsync<MercadoPagoPaymentSearchResponse>(JsonOptions, cancellationToken)
            ?? throw new PaymentGatewayException("Mercado Pago returned an empty payment search response.");

        var approved = body.Results.FirstOrDefault(p => string.Equals(p.Status, "approved", StringComparison.OrdinalIgnoreCase));
        return approved is null
            ? null
            : new PaymentInfo(approved.Id.ToString(), approved.Status, approved.ExternalReference, approved.TransactionAmount, approved.CurrencyId);
    }

    /// <summary>Builds the success/failure/pending redirect targets (design section 10, sequence diagram); <see langword="null"/> when no frontend base URL is configured.</summary>
    private static MercadoPagoBackUrls? BuildBackUrls(string frontendBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(frontendBaseUrl))
        {
            return null;
        }

        var baseUrl = frontendBaseUrl.TrimEnd('/');
        return new MercadoPagoBackUrls(
            $"{baseUrl}/checkout/result?status=approved",
            $"{baseUrl}/checkout/result?status=failure",
            $"{baseUrl}/checkout/result?status=pending");
    }
}

internal sealed record MercadoPagoPreferenceItem(
    string Title,
    int Quantity,
    [property: JsonPropertyName("unit_price")] decimal UnitPrice,
    [property: JsonPropertyName("currency_id")] string CurrencyId);

internal sealed record MercadoPagoBackUrls(
    [property: JsonPropertyName("success")] string Success,
    [property: JsonPropertyName("failure")] string Failure,
    [property: JsonPropertyName("pending")] string Pending);

internal sealed record MercadoPagoPreferenceRequest(
    [property: JsonPropertyName("external_reference")] string ExternalReference,
    IReadOnlyList<MercadoPagoPreferenceItem> Items,
    [property: JsonPropertyName("notification_url")] string? NotificationUrl,
    [property: JsonPropertyName("back_urls")] MercadoPagoBackUrls? BackUrls = null,
    [property: JsonPropertyName("auto_return")] string? AutoReturn = null);

internal sealed record MercadoPagoPreferenceResponse(
    string Id,
    [property: JsonPropertyName("init_point")] string InitPoint);

/// <summary>Raw Mercado Pago payment resource shape (design section 6b); <see cref="Status"/> is passed through unparsed.</summary>
internal sealed record MercadoPagoPaymentResponse(
    long Id,
    string Status,
    [property: JsonPropertyName("external_reference")] string ExternalReference,
    [property: JsonPropertyName("transaction_amount")] decimal TransactionAmount,
    [property: JsonPropertyName("currency_id")] string CurrencyId);

/// <summary><c>GET /v1/payments/search</c> response envelope — only <c>results</c> is used.</summary>
internal sealed record MercadoPagoPaymentSearchResponse(IReadOnlyList<MercadoPagoPaymentResponse> Results);
