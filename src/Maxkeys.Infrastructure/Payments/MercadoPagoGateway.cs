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
    private readonly ILogger<MercadoPagoGateway> _logger;

    public MercadoPagoGateway(HttpClient httpClient, IOptionsMonitor<MercadoPagoOptions> options, ILogger<MercadoPagoGateway> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<PaymentPreference> CreatePreferenceAsync(Order order, CancellationToken cancellationToken = default)
    {
        var notificationUrl = _options.CurrentValue.NotificationUrl;
        var request = new MercadoPagoPreferenceRequest(
            order.Id.ToString(),
            order.Items
                .Select(item => new MercadoPagoPreferenceItem(
                    $"{item.ProductNameSnapshot} - {item.VariantNameSnapshot}", item.Quantity, item.UnitPrice, order.Currency))
                .ToList(),
            string.IsNullOrWhiteSpace(notificationUrl) ? null : notificationUrl);

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
}

internal sealed record MercadoPagoPreferenceItem(
    string Title,
    int Quantity,
    [property: JsonPropertyName("unit_price")] decimal UnitPrice,
    [property: JsonPropertyName("currency_id")] string CurrencyId);

internal sealed record MercadoPagoPreferenceRequest(
    [property: JsonPropertyName("external_reference")] string ExternalReference,
    IReadOnlyList<MercadoPagoPreferenceItem> Items,
    [property: JsonPropertyName("notification_url")] string? NotificationUrl);

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
