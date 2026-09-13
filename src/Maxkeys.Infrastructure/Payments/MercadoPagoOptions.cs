namespace Maxkeys.Infrastructure.Payments;

/// <summary>
/// Binds the <c>Payments</c> config section (design section 10) used by the
/// real Mercado Pago integration: <see cref="AccessToken"/> selects
/// <see cref="MercadoPagoGateway"/> over <c>NotConfiguredPaymentGateway</c>
/// when non-empty (design section 3/9), <see cref="WebhookSecret"/> feeds
/// <see cref="MercadoPagoSignatureValidator"/>, and <see cref="WebhookEnabled"/>
/// is the operator kill switch (payments-webhook spec "Kill Switch").
/// </summary>
public sealed class MercadoPagoOptions
{
    public const string SectionName = "Payments";

    public string AccessToken { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public bool WebhookEnabled { get; set; } = true;

    public string NotificationUrl { get; set; } = string.Empty;
}
