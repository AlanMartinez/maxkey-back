using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Carousel;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Notifications;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IAppDbContext"/> (ADR-01). Snake_case
/// naming (ADR-16) is applied in <see cref="OnConfiguring"/> so both the design-time
/// factory and DI-registered instances get it consistently.
/// </summary>
public sealed class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<CarouselSlide> CarouselSlides => Set<CarouselSlide>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Key> Keys => Set<Key>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<ProcessedWebhookNotification> ProcessedWebhookNotifications => Set<ProcessedWebhookNotification>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
