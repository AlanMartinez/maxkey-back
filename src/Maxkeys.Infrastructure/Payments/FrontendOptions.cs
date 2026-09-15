namespace Maxkeys.Infrastructure.Payments;

/// <summary>
/// Binds the <c>Frontend</c> config section (design section 10). <see cref="BaseUrl"/>
/// is the deployed Nuxt origin (Vercel in production) used to build the Mercado Pago
/// <c>back_urls</c> in <see cref="MercadoPagoGateway.CreatePreferenceAsync"/> — empty
/// skips <c>back_urls</c> entirely so local/CI runs without a frontend still work.
/// </summary>
public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    public string BaseUrl { get; set; } = string.Empty;
}
