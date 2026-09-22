using System.Net.Http.Headers;
using Maxkeys.Application.Buyers;
using Maxkeys.Application.Carousel;
using Maxkeys.Application.Catalog;
using Maxkeys.Application.Checkout;
using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Orders;
using Maxkeys.Application.Outbox;
using Maxkeys.Application.Payments;
using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Application.Wishlist;
using Maxkeys.Infrastructure.Email;
using Maxkeys.Infrastructure.Outbox;
using Maxkeys.Infrastructure.Payments;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Maxkeys.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer (design section 2/3), wired
/// from <c>Maxkeys.Api/Program.cs</c> via a single <see cref="AddInfrastructure"/>
/// call so the API project stays a thin host.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IOutboxEventReader, OutboxEventReader>();

        services.AddKeyCipher(configuration);
        services.AddStorageUrlBuilder(configuration);

        AddEmailSender(services, configuration);

        services.AddOutboxHandlers();
        services.AddOutboxProcessor(configuration);

        AddPaymentGateway(services, configuration);
        services.AddPaymentReconciliation(configuration);
        AddUseCases(services);

        return services;
    }

    /// <summary>
    /// One class per use case, registered in DI (ADR-02) so minimal API endpoint
    /// parameters resolve them as services rather than inferring an (invalid) body
    /// parameter — every use case class used by an endpoint MUST be registered here.
    /// </summary>
    private static void AddUseCases(IServiceCollection services)
    {
        services.AddScoped<GetCatalog>();
        services.AddScoped<GetProductBySlug>();
        services.AddScoped<ListAdminProducts>();
        services.AddScoped<CreateProduct>();
        services.AddScoped<UpdateProduct>();
        services.AddScoped<DeleteProduct>();
        services.AddScoped<CreateProductVariant>();
        services.AddScoped<UpdateProductVariant>();
        services.AddScoped<DeleteProductVariant>();
        services.AddScoped<GetCarousel>();
        services.AddScoped<ListCarouselSlides>();
        services.AddScoped<CreateCarouselSlide>();
        services.AddScoped<UpdateCarouselSlide>();
        services.AddScoped<DeleteCarouselSlide>();
        services.AddScoped<CreateOrder>();
        services.AddScoped<GetOrderStatus>();
        services.AddScoped<GetMyOrders>();
        services.AddScoped<GetMyOrder>();
        services.AddScoped<ListNotifications>();
        services.AddScoped<ReadNotification>();
        services.AddScoped<ListOrdersAwaitingFulfillment>();
        services.AddScoped<AttachKeyToOrderItem>();
        services.AddScoped<AssignVaultKeysToOrder>();
        services.AddScoped<DeliverOrder>();
        services.AddScoped<LoadVaultKeys>();
        services.AddScoped<ToggleProductVault>();
        services.AddScoped<ListVaultStock>();
        services.AddScoped<ListVariantKeys>();
        services.AddScoped<RequestDeliveryResend>();
        services.AddScoped<ProcessPaymentNotification>();
        services.AddScoped<ListBuyers>();
        services.AddScoped<GetAdminOrderDetail>();
        services.AddScoped<RevealOrderItemKeys>();
        services.AddScoped<AddToWishlist>();
        services.AddScoped<RemoveFromWishlist>();
    }

    /// <summary>
    /// Registers both candidate <see cref="IEmailSender"/> implementations plus a
    /// scoped factory that picks between them by reading
    /// <see cref="EmailOptions.Sender"/> lazily via <see cref="IOptionsMonitor{T}"/>
    /// at resolution time — the same lazy-selection pattern (and the same
    /// rationale: <c>WebApplicationFactory</c> config overrides are only visible
    /// after the host finishes building) used for <see cref="AddPaymentGateway"/>
    /// in PR11.
    /// </summary>
    private static void AddEmailSender(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));

        services.AddScoped<LoggingEmailSender>();
        services.AddScoped<SmtpEmailSender>();

        services.AddScoped<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<EmailOptions>>().CurrentValue;
            return options.Sender.Equals("Smtp", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<SmtpEmailSender>()
                : sp.GetRequiredService<LoggingEmailSender>();
        });
    }

    /// <summary>
    /// Registers both candidate gateways plus a scoped <see cref="IPaymentGateway"/>
    /// factory that picks between them by reading <see cref="MercadoPagoOptions.AccessToken"/>
    /// *lazily*, at resolution time — not by reading raw configuration once during
    /// this method call. <c>WebApplicationFactory</c>-based tests (and any host
    /// customization that layers configuration on top of <c>builder.Configuration</c>
    /// after <c>Program.cs</c> calls <c>AddInfrastructure</c>) only see their overrides
    /// once the host finishes building; an eager read here would silently see the
    /// pre-override value. When <c>Payments:AccessToken</c> is empty,
    /// <see cref="NotConfiguredPaymentGateway"/> is used, so <c>POST /checkout/orders</c>
    /// builds and runs green with no Mercado Pago credentials (design section 3/9, PR9).
    /// Otherwise <see cref="MercadoPagoGateway"/> is used, backed by a typed
    /// <c>HttpClient</c> (ADR-10) whose base address and bearer token are likewise
    /// configured lazily via the <c>(IServiceProvider, HttpClient)</c> overload.
    /// <see cref="MercadoPagoOptions"/> and <see cref="MercadoPagoSignatureValidator"/>
    /// are always registered — the webhook endpoint needs them (kill switch, signature
    /// check) independently of which gateway is selected. <see cref="FakePaymentGateway"/>
    /// (local demo mode, docs/local-demo.md) is selected only when <c>Payments:Mode</c>
    /// is <c>Fake</c> AND the host environment is Development; in any other environment
    /// the value is ignored and the production selection above applies unchanged.
    /// </summary>
    private static void AddPaymentGateway(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MercadoPagoOptions>().Bind(configuration.GetSection(MercadoPagoOptions.SectionName));
        services.AddOptions<FrontendOptions>().Bind(configuration.GetSection(FrontendOptions.SectionName));
        services.AddSingleton<MercadoPagoSignatureValidator>();

        services.AddScoped<NotConfiguredPaymentGateway>();
        services.AddSingleton<FakePaymentGateway>();
        services.AddHttpClient<MercadoPagoGateway>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<MercadoPagoOptions>>().CurrentValue;
            client.BaseAddress = new Uri("https://api.mercadopago.com");
            if (!string.IsNullOrWhiteSpace(options.AccessToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
            }
        });

        services.AddScoped<IPaymentGateway>(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<MercadoPagoOptions>>().CurrentValue;
            if (options.IsFakeMode && sp.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
                return sp.GetRequiredService<FakePaymentGateway>();
            }

            return string.IsNullOrWhiteSpace(options.AccessToken)
                ? sp.GetRequiredService<NotConfiguredPaymentGateway>()
                : sp.GetRequiredService<MercadoPagoGateway>();
        });
    }
}
