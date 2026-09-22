using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>
/// Covers <see cref="AssignVaultKeysToOrder"/> (admin-key-delivery-gate spec:
/// decision 3, "Asignar"; vault spec: Vault Auto-Assignment On Approval —
/// extracted from <c>OrderApprovedHandler</c> so both the automatic and the
/// admin-manual path share one implementation): all-or-nothing per item, a
/// no-op for any order not <see cref="OrderStatus.AwaitingFulfillment"/>, and
/// exactly one winner when two orders race the same last key.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AssignVaultKeysToOrderTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public AssignVaultKeysToOrderTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Full_vault_stock_assigns_every_key_and_reaches_keys_assigned()
    {
        var variantId = await SeedVaultVariantAsync();
        await LoadVaultKeysAsync(variantId, "CODE-1", "CODE-2");
        var orderId = await SeedAwaitingFulfillmentOrderAsync(variantId, quantity: 2);

        await using var context = _fixture.CreateContext();
        var result = await new AssignVaultKeysToOrder(context).ExecuteAsync(orderId);

        Assert.NotNull(result);
        Assert.True(result!.AllItemsComplete);
        Assert.Equal(OrderStatus.KeysAssigned, result.OrderStatus);

        var order = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.KeysAssigned, order.Status);
        Assert.Null(order.DeliveredAt);
        Assert.All(order.Items.Single().Keys, k => Assert.Equal(KeyStatus.Assigned, k.Status));
    }

    [Fact]
    public async Task Partial_vault_stock_skips_that_item_all_or_nothing()
    {
        var variantId = await SeedVaultVariantAsync();
        await LoadVaultKeysAsync(variantId, "CODE-1", "CODE-2"); // only 2 of the 3 needed
        var orderId = await SeedAwaitingFulfillmentOrderAsync(variantId, quantity: 3);

        await using var context = _fixture.CreateContext();
        var result = await new AssignVaultKeysToOrder(context).ExecuteAsync(orderId);

        Assert.NotNull(result);
        Assert.False(result!.AllItemsComplete);
        Assert.Equal(OrderStatus.AwaitingFulfillment, result.OrderStatus);
        Assert.Equal(0, result.Items.Single().AssignedKeys);

        var availableRemaining = await context.Keys.CountAsync(k => k.ProductVariantId == variantId && k.Status == KeyStatus.Available);
        Assert.Equal(2, availableRemaining); // stock untouched
    }

    [Fact]
    public async Task Full_aggregate_stock_assigns_each_item_when_order_repeats_a_variant()
    {
        var variantId = await SeedVaultVariantAsync();
        await LoadVaultKeysAsync(variantId, "CODE-1", "CODE-2");
        var orderId = await SeedAwaitingFulfillmentOrderAsync(variantId, quantities: [1, 1]);

        await using var context = _fixture.CreateContext();
        var result = await new AssignVaultKeysToOrder(context).ExecuteAsync(orderId);

        Assert.NotNull(result);
        Assert.True(result!.AllItemsComplete);
        Assert.Equal(OrderStatus.KeysAssigned, result.OrderStatus);
        Assert.All(result.Items, item => Assert.Equal(item.Quantity, item.AssignedKeys));
    }

    [Fact]
    public async Task Vault_disabled_product_never_auto_assigns()
    {
        await using var context = _fixture.CreateContext();
        var product = new Product($"p-{Guid.NewGuid():N}", "Non-Vault Product", $"platform-{Guid.NewGuid():N}");
        context.Products.Add(product);
        var variant = new ProductVariant(product.Id, 1_000m, "ARS", region: "AR", edition: "Standard");
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();

        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        await new LoadVaultKeys(context, cipher).ExecuteAsync(variant.Id, ["CODE-1"], "seed-admin");

        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(variant.Id, "Non-Vault Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        await using var runContext = _fixture.CreateContext();
        var result = await new AssignVaultKeysToOrder(runContext).ExecuteAsync(order.Id);

        Assert.NotNull(result);
        Assert.False(result!.AllItemsComplete);
        Assert.Equal(0, result.Items.Single().AssignedKeys);
    }

    [Fact]
    public async Task Unknown_order_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var result = await new AssignVaultKeysToOrder(context).ExecuteAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task Order_not_awaiting_fulfillment_is_a_noop_returning_current_state()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = _fixture.CreateContext();
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var result = await new AssignVaultKeysToOrder(context).ExecuteAsync(order.Id);

        Assert.NotNull(result);
        Assert.Equal(OrderStatus.Paid, result!.OrderStatus);
        Assert.False(result.AllItemsComplete);
    }

    [Fact]
    public async Task Concurrent_assign_for_two_orders_racing_the_same_last_key_yields_exactly_one_full_assignment()
    {
        var variantId = await SeedVaultVariantAsync();
        await LoadVaultKeysAsync(variantId, "ONLY-CODE");

        var orderAId = await SeedAwaitingFulfillmentOrderAsync(variantId, quantity: 1);
        var orderBId = await SeedAwaitingFulfillmentOrderAsync(variantId, quantity: 1);

        await using var contextA = _fixture.CreateContext();
        await using var contextB = _fixture.CreateContext();
        var sutA = new AssignVaultKeysToOrder(contextA);
        var sutB = new AssignVaultKeysToOrder(contextB);

        var results = await Task.WhenAll(sutA.ExecuteAsync(orderAId), sutB.ExecuteAsync(orderBId));

        Assert.Single(results, r => r!.AllItemsComplete);
        Assert.Single(results, r => !r!.AllItemsComplete);

        await using var verifyContext = _fixture.CreateContext();
        var availableRemaining = await verifyContext.Keys.CountAsync(k => k.ProductVariantId == variantId && k.Status == KeyStatus.Available);
        Assert.Equal(0, availableRemaining);
    }

    private async Task<Guid> SeedVaultVariantAsync()
    {
        await using var context = _fixture.CreateContext();
        var product = new Product($"p-{Guid.NewGuid():N}", "Vault Product", $"platform-{Guid.NewGuid():N}");
        product.SetVaultEnabled(true);
        context.Products.Add(product);
        var variant = new ProductVariant(product.Id, 1_000m, "ARS", region: "AR", edition: "Standard");
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();
        return variant.Id;
    }

    private async Task LoadVaultKeysAsync(Guid variantId, params string[] codes)
    {
        await using var context = _fixture.CreateContext();
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        await new LoadVaultKeys(context, cipher).ExecuteAsync(variantId, codes, "seed-admin");
    }

    private Task<Guid> SeedAwaitingFulfillmentOrderAsync(Guid variantId, int quantity) =>
        SeedAwaitingFulfillmentOrderAsync(variantId, [quantity]);

    private async Task<Guid> SeedAwaitingFulfillmentOrderAsync(Guid variantId, IReadOnlyList<int> quantities)
    {
        await using var context = _fixture.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var lines = quantities.Select(quantity => new OrderLine(variantId, "Vault Product", "Standard", 1_000m, quantity)).ToList();
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", lines, now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
}
