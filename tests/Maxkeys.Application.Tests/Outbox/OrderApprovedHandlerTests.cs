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
    public async Task Vault_enabled_with_full_stock_reaches_keys_assigned_and_still_sends_the_operator_email()
    {
        var (orderId, variantId) = await SeedPaidOrderWithVaultProductAsync(quantity: 2, vaultEnabled: true);
        await LoadVaultKeysAsync(variantId, "CODE-1", "CODE-2");

        var emailSender = new RecordingEmailSender();
        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await sut.HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.KeysAssigned, order.Status);
        Assert.Null(order.DeliveredAt);
        Assert.All(order.Items.Single().Keys, k => Assert.Equal(KeyStatus.Assigned, k.Status));
        Assert.Single(emailSender.SentMessages);
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
        return new OrderApprovedHandler(context, new AssignVaultKeysToOrder(context), emailSender, options, NullLogger<OrderApprovedHandler>.Instance);
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

    /// <summary>
    /// Regression test for the whole-branch review finding: <c>OrderApprovedHandler</c>'s
    /// concurrency retry used to call a full <c>ChangeTracker.Clear()</c>
    /// (<c>ClearTracker</c>, now <c>DetachHandlerState</c>), which — because
    /// <c>OutboxProcessor.ProcessOnceAsync</c> shares ONE <see cref="AppDbContext"/> across a
    /// whole claimed batch (<c>OutboxProcessor.cs:62-72</c>) — detached every other claimed
    /// <see cref="OutboxEvent"/> row too, silently dropping their later
    /// <c>MarkProcessed</c>/<c>MarkFailedAttempt</c> writes.
    ///
    /// Forces a genuine <see cref="DbUpdateConcurrencyException"/> deterministically (no real
    /// threading) by racing a second, separate <see cref="AppDbContext"/> that bumps the
    /// target order's row (and therefore its <c>xmin</c> token) between the handler's read and
    /// write. Then replays what <see cref="Infrastructure.Outbox.OutboxProcessor.DispatchAsync"/>
    /// would do next in the same pass for a sibling event, on the SAME shared context the
    /// handler used, and asserts that sibling's status mutation actually persisted.
    /// </summary>
    [Fact]
    public async Task Concurrency_retry_does_not_lose_a_sibling_outbox_events_status_update()
    {
        var orderId = await SeedPaidOrderAsync();

        Guid siblingEventId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var seededSiblingEvent = new OutboxEvent(OutboxEventTypes.OrderApproved, $$"""{"orderId":"{{Guid.NewGuid()}}"}""", DateTimeOffset.UtcNow);
            seedContext.OutboxEvents.Add(seededSiblingEvent);
            await seedContext.SaveChangesAsync();
            siblingEventId = seededSiblingEvent.Id;
        }

        var emailSender = new RecordingEmailSender();

        // `db` stands in for OutboxProcessor.ProcessOnceAsync's single shared batch AppDbContext:
        // both the target order and the sibling event get tracked through it, exactly as
        // OutboxClaimQuery.ClaimBatchAsync would have loaded them for a real claimed batch.
        await using var db = _fixture.CreateContext();
        await db.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        var siblingEvent = await db.OutboxEvents.SingleAsync(e => e.Id == siblingEventId);

        // A separate context bumps the same order row (and therefore its xmin) without going
        // through `db`'s identity map, leaving `db`'s tracked copy of the order with a now-stale
        // xmin token — this is what makes the handler's own SaveChangesAsync throw
        // DbUpdateConcurrencyException on its first attempt below.
        await using (var racer = _fixture.CreateContext())
        {
            await racer.Database.ExecuteSqlInterpolatedAsync($"UPDATE orders SET updated_at = now() WHERE id = {orderId}");
        }

        var sut = CreateHandler(db, emailSender);
        await sut.HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        // Retry succeeded transparently — no exception escaped HandleAsync. Verify through a FRESH
        // context rather than an in-memory reference: the tracked order already had
        // MarkAwaitingFulfillment applied in-memory during the FIRST, failed attempt, so asserting
        // against an in-memory instance wouldn't prove the retry's SaveChangesAsync actually persisted anything.
        await using (var verifyOrderContext = _fixture.CreateContext())
        {
            var persistedOrder = await verifyOrderContext.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal(OrderStatus.AwaitingFulfillment, persistedOrder.Status);
        }

        // Replay what OutboxProcessor.DispatchAsync (OutboxProcessor.cs:87-98) does next in the
        // same pass for the sibling event, on the SAME shared `db` — this is the assertion that
        // fails before the fix: DetachHandlerState (formerly ClearTracker) used to detach
        // `siblingEvent` as a side effect of the order's retry, so this SaveChangesAsync would
        // silently persist nothing for it.
        siblingEvent.MarkProcessed(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(CancellationToken.None);

        await using var verifyContext = _fixture.CreateContext();
        var persistedSibling = await verifyContext.OutboxEvents.SingleAsync(e => e.Id == siblingEventId);
        Assert.Equal(OutboxEventStatus.Processed, persistedSibling.Status);
    }
}
