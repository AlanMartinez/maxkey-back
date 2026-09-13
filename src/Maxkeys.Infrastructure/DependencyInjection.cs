using Maxkeys.Application.Catalog;
using Maxkeys.Application.Checkout;
using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Orders;
using Maxkeys.Application.Outbox;
using Maxkeys.Application.Payments;
using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Infrastructure.Email;
using Maxkeys.Infrastructure.Outbox;
using Maxkeys.Infrastructure.Payments;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddKeyCipher(configuration);
        services.AddStorageUrlBuilder(configuration);

        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.AddScoped<IEmailSender, LoggingEmailSender>(); // SmtpEmailSender + Email:Sender selection arrive in PR12.

        services.AddOutboxHandlers();
        services.AddOutboxProcessor(configuration);

        AddPaymentGateway(services, configuration);
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
        services.AddScoped<CreateOrder>();
        services.AddScoped<GetOrderStatus>();
        services.AddScoped<GetMyOrders>();
        services.AddScoped<GetMyOrder>();
        services.AddScoped<ListOrdersAwaitingFulfillment>();
        services.AddScoped<AttachKeyToOrderItem>();
    }

    /// <summary>
    /// <see cref="NotConfiguredPaymentGateway"/> is registered when
    /// <c>Payments:AccessToken</c> is empty, so <c>POST /checkout/orders</c> builds
    /// and runs green with no Mercado Pago credentials (design section 3/9, PR9).
    /// PR11 adds the non-empty branch registering the real <c>MercadoPagoGateway</c>.
    /// </summary>
    private static void AddPaymentGateway(IServiceCollection services, IConfiguration configuration)
    {
        var accessToken = configuration["Payments:AccessToken"];
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            services.AddScoped<IPaymentGateway, NotConfiguredPaymentGateway>();
        }
    }
}
