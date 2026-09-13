using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Registers every <see cref="IOutboxHandler"/> so <c>OutboxProcessor</c>
/// (PR7b, Infrastructure) can resolve <c>IEnumerable&lt;IOutboxHandler&gt;</c>
/// and dispatch by <see cref="IOutboxHandler.EventType"/> without knowing the
/// concrete handler types. A future PR adds <c>OrderDeliveredHandler</c> here.
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddOutboxHandlers(this IServiceCollection services)
    {
        services.AddScoped<IOutboxHandler, OrderApprovedHandler>();

        return services;
    }
}
