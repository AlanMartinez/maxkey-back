using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Outbox;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Outbox;

/// <summary>
/// Covers <see cref="OrderApprovedHandler"/> (outbox-processing spec:
/// OrderApproved Handler; tasks.md 7.6): <c>Paid</c> → <c>AwaitingFulfillment</c>
/// with exactly one operator email; already-transitioned orders are a no-op;
/// an unknown order throws so the outbox processor can retry/dead-letter.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderApprovedHandlerTests
{
    private const string OperatorAddress = "ops@maxkeys.test";

    private readonly PostgresFixture _fixture;

    public OrderApprovedHandlerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Paid_order_transitions_to_awaiting_fulfillment_and_sends_one_operator_email()
    {
        var orderId = await SeedPaidOrderAsync();
        var emailSender = new RecordingEmailSender();

        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await sut.HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await context.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);

        var email = Assert.Single(emailSender.SentMessages);
        Assert.Equal(OperatorAddress, email.To);
        Assert.Contains(orderId.ToString(), email.Subject + email.TextBody);
        Assert.DoesNotContain("EncryptedCode", email.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nonce", email.TextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Already_awaiting_fulfillment_is_a_noop_with_no_email()
    {
        var orderId = await SeedPaidOrderAsync();
        var emailSender = new RecordingEmailSender();

        await using var firstContext = _fixture.CreateContext();
        await CreateHandler(firstContext, emailSender).HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);
        Assert.Single(emailSender.SentMessages);

        await using var secondContext = _fixture.CreateContext();
        await CreateHandler(secondContext, emailSender).HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await secondContext.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
        Assert.Single(emailSender.SentMessages);
    }

    [Fact]
    public async Task Unknown_order_throws()
    {
        var emailSender = new RecordingEmailSender();
        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.HandleAsync(OrderApprovedEvent(Guid.NewGuid()), CancellationToken.None));

        Assert.Empty(emailSender.SentMessages);
    }

    [Fact]
    public async Task Vault_enabled_with_full_stock_auto_delivers_and_skips_the_operator_email()
    {
        var (orderId, variantId) = await SeedPaidOrderWithVaultProductAsync(quantity: 2, vaultEnabled: true);
        await LoadVaultKeysAsync(variantId, "CODE-1", "CODE-2");

        var emailSender = new RecordingEmailSender();
        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await sut.HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveredAt);
        Assert.All(order.Items.Single().Keys, k => Assert.Equal(KeyStatus.Assigned, k.Status));
        Assert.Empty(emailSender.SentMessages);

        var deliveredEvents = await context.OutboxEvents.Where(e => e.Type == OutboxEventTypes.OrderDelivered).ToListAsync();
        Assert.Single(deliveredEvents, e => e.Payload.Contains(orderId.ToString()));
    }

    [Fact]
    public async Task Vault_enabled_with_partial_stock_skips_that_item_and_still_sends_the_operator_email()
    {
        var (orderId, variantId) = await SeedPaidOrderWithVaultProductAsync(quantity: 3, vaultEnabled: true);
        await LoadVaultKeysAsync(variantId, "CODE-1", "CODE-2"); // only 2 of the 3 needed

        var emailSender = new RecordingEmailSender();
        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await sut.HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
        Assert.Empty(order.Items.Single().Keys); // all-or-nothing: zero keys assigned, not 2 of 3
        Assert.Single(emailSender.SentMessages);

        var availableRemaining = await context.Keys.CountAsync(k => k.ProductVariantId == variantId && k.Status == KeyStatus.Available);
        Assert.Equal(2, availableRemaining); // stock untouched
    }

    [Fact]
    public async Task Vault_disabled_never_auto_assigns_even_with_stock_available()
    {
        var (orderId, variantId) = await SeedPaidOrderWithVaultProductAsync(quantity: 1, vaultEnabled: false);
        await LoadVaultKeysAsync(variantId, "CODE-1");

        var emailSender = new RecordingEmailSender();
        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await sut.HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
        Assert.Empty(order.Items.Single().Keys);
        Assert.Single(emailSender.SentMessages);
    }

    private async Task<(Guid OrderId, Guid VariantId)> SeedPaidOrderWithVaultProductAsync(int quantity, bool vaultEnabled)
    {
        Guid variantId;
        await using (var context = _fixture.CreateContext())
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Vault Product", $"platform-{Guid.NewGuid():N}");
            product.SetVaultEnabled(vaultEnabled);
            context.Products.Add(product);
            var variant = new ProductVariant(product.Id, 1_000m, "ARS", region: "AR", edition: "Standard");
            context.ProductVariants.Add(variant);
            await context.SaveChangesAsync();
            variantId = variant.Id;
        }

        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(variantId, "Vault Product", "Standard", 1_000m, quantity)],
            now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);

        await using (var context = _fixture.CreateContext())
        {
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        return (order.Id, variantId);
    }

    private async Task LoadVaultKeysAsync(Guid variantId, params string[] codes)
    {
        await using var context = _fixture.CreateContext();
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions
        {
            EncryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
            CurrentVersion = 1,
        }));
        await new LoadVaultKeys(context, cipher).ExecuteAsync(variantId, codes, "seed-admin");
    }

    private static OrderApprovedHandler CreateHandler(AppDbContext context, RecordingEmailSender emailSender)
    {
        var options = Options.Create(new EmailOptions { OperatorTo = OperatorAddress });
        return new OrderApprovedHandler(context, emailSender, options, NullLogger<OrderApprovedHandler>.Instance);
    }

    private static OutboxEvent OrderApprovedEvent(Guid orderId) =>
        new(OutboxEventTypes.OrderApproved, $$"""{"orderId":"{{orderId}}"}""", DateTimeOffset.UtcNow);

    private async Task<Guid> SeedPaidOrderAsync()
    {
        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(Guid.NewGuid(), "Product", "Standard", 2_500m, 2)],
            DateTimeOffset.UtcNow);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
}
