using Maxkeys.Domain.Carousel;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Persistence;

/// <summary>
/// Application-owned persistence seam (ADR-01): Application references EF Core
/// abstractions only for <see cref="DbSet{TEntity}"/> here, never Infrastructure.
/// Implemented by <c>Maxkeys.Infrastructure.Persistence.AppDbContext</c>.
/// </summary>
public interface IAppDbContext
{
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<CarouselSlide> CarouselSlides { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<Key> Keys { get; }
    DbSet<OutboxEvent> OutboxEvents { get; }
    DbSet<ProcessedWebhookNotification> ProcessedWebhookNotifications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
