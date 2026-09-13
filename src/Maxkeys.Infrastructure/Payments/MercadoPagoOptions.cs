namespace Maxkeys.Infrastructure.Payments;

/// <summary>
/// Binds the <c>Payments</c> config section (design section 10) used by the
/// real Mercado Pago integration: <see cref="AccessToken"/> selects
/// <see cref="MercadoPagoGateway"/> over <c>NotConfiguredPaymentGateway</c>
/// when non-empty (design section 3/9), <see cref="WebhookSecret"/> feeds
/// <see cref="MercadoPagoSignatureValidator"/>, and <see cref="WebhookEnabled"/>
/// is the operator kill switch (payments-webhook spec "Kill Switch").
/// <see cref="Mode"/> and the <c>Fake*</c> properties belong to the local demo
/// mode (docs/local-demo.md) and are ignored outside the Development environment.
/// </summary>
public sealed class MercadoPagoOptions
{
    public const string SectionName = "Payments";

    /// <summary>Value of <see cref="Mode"/> that selects <see cref="FakePaymentGateway"/> in Development.</summary>
    public const string FakeMode = "Fake";

    public string AccessToken { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public bool WebhookEnabled { get; set; } = true;

    public string NotificationUrl { get; set; } = string.Empty;

    /// <summary>
    /// Empty (default) keeps the production selection: <see cref="MercadoPagoGateway"/>
    /// when <see cref="AccessToken"/> is set, <c>NotConfiguredPaymentGateway</c> otherwise.
    /// <c>Fake</c> selects <see cref="FakePaymentGateway"/> — only honoured in Development.
    /// </summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Base URL of this API as seen by the browser; the fake init point is <c>{base}/dev/payments/{orderId}</c>.</summary>
    public string FakeInitPointBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>
    /// Frontend result page the fake approval redirects to; <c>{orderId}</c> is replaced
    /// with the order id. Mirrors the query parameters the real Mercado Pago back_url carries.
    /// </summary>
    public string FakeReturnUrl { get; set; } = "http://localhost:3000/checkout/result?orderId={orderId}&status=approved";

    public bool IsFakeMode => string.Equals(Mode, FakeMode, StringComparison.OrdinalIgnoreCase);
}
