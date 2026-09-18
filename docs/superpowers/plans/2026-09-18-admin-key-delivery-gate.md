# Admin Key Delivery Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gate buyer key visibility behind explicit admin review/delivery, and behind an explicit per-item buyer "revelar key" action, without changing the existing atomic auto-assignment on payment approval.

**Architecture:** Insert a new `OrderStatus.KeysAssigned` between `AwaitingFulfillment` and `Delivered` so `Order.AttachKey` stops there instead of auto-completing the order; add an explicit admin `Order.MarkDelivered` action that advances to `Delivered` with no outbox/email side effect; add a new `KeyStatus.Revealed` + `Key.Reveal` so a buyer's "revelar key" click is the only thing that ever exposes a plaintext code, once per item, permanently. Extract the existing per-item all-or-nothing vault-assignment loop out of `OrderApprovedHandler` into its own reusable use case so both the automatic (payment-approval) and manual (admin "Asignar" button) paths share one implementation.

**Tech Stack:** .NET 8, ASP.NET Core Minimal APIs, EF Core (Npgsql), xUnit + Testcontainers (real Postgres), Clean Architecture (Domain/Application/Infrastructure/Api).

**Spec:** `docs/superpowers/specs/2026-09-18-admin-key-delivery-gate-design.md`

## Global Constraints

- `OrderStatus` and `KeyStatus` are persisted as text (`HasConversion<string>()`, `KeyConfiguration.cs`/`OrderConfiguration.cs`) — inserting new enum members anywhere in the C# declaration is safe, no data migration needed for the enums themselves.
- No feature flag for a future full-auto-with-email mode — out of scope (YAGNI), a separate change if requested later.
- No automatic email on delivery — `MarkDelivered` has no outbox/email side effect. The existing `RequestDeliveryResend`/`OrderDeliveryResendHandler` manual-email path is untouched.
- Reveal is per order item (not per individual key): one buyer action reveals every key under that item.
- Every new admin/buyer mutation gets an `ILogger` line recording who did what to which order/item (key-lifecycle audit requirement).
- Follow existing repo conventions exactly: one class per use case (ADR-02), `null` return for "not found" mapped to 404 by the endpoint, `DomainException`→422 / `DomainConflictException`→409 via the existing global `ProblemDetailsExceptionHandler` (no per-endpoint try/catch needed), real-Postgres `[Collection(PostgresCollection.Name)]` tests for Application-layer use cases, `[Collection(Hs256ApiCollection.Name)]` + `Hs256ApiTestFixture` for API-layer tests.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Maxkeys.Domain/Orders/OrderStatus.cs` | Add `KeysAssigned` |
| `src/Maxkeys.Domain/Orders/Order.cs` | `AttachKey` completion → `KeysAssigned`; new `MarkDelivered` |
| `src/Maxkeys.Domain/Keys/KeyStatus.cs` | Add `Revealed` |
| `src/Maxkeys.Domain/Keys/Key.cs` | New `RevealedAt` + `Reveal` |
| `src/Maxkeys.Application/Fulfillment/AssignVaultKeysToOrder.cs` (new) | Extracted, reusable all-or-nothing vault assignment for one order |
| `src/Maxkeys.Application/Outbox/OrderApprovedHandler.cs` | Delegates to `AssignVaultKeysToOrder`; always emails the operator |
| `src/Maxkeys.Application/Fulfillment/AttachKeyToOrderItem.cs` | Stops enqueueing `OrderDelivered` |
| `src/Maxkeys.Application/Fulfillment/DeliverOrder.cs` (new) | Admin "Entregar" action |
| `src/Maxkeys.Application/Orders/RevealOrderItemKeys.cs` (new) | Buyer "revelar key" action |
| `src/Maxkeys.Application/Orders/GetMyOrder.cs` | Only ever returns already-`Revealed` codes; adds `Revealable` |
| `src/Maxkeys.Application/Orders/OrdersDtos.cs` | `MyOrderItemDetail` gains `Revealable`, `Keys` becomes non-nullable |
| `src/Maxkeys.Application/Buyers/ListBuyers.cs` / `BuyersDtos.cs` | `AdminBuyerOrderItem` gains `RevealedKeys` |
| `src/Maxkeys.Application/Outbox/OrderDeliveredHandler.cs` | **Deleted** — nothing enqueues `OrderDelivered` any more |
| `src/Maxkeys.Domain/Outbox/OutboxEventTypes.cs` | Remove unused `OrderDelivered` constant |
| `src/Maxkeys.Application/Outbox/OutboxServiceCollectionExtensions.cs` | Remove `OrderDeliveredHandler` registration |
| `src/Maxkeys.Api/Endpoints/AdminEndpoints.cs` | New `assign-keys`, `deliver` routes |
| `src/Maxkeys.Api/Endpoints/MeEndpoints.cs` | New reveal route |
| `src/Maxkeys.Infrastructure/DependencyInjection.cs` | Register `AssignVaultKeysToOrder`, `DeliverOrder`, `RevealOrderItemKeys` |
| `src/Maxkeys.Infrastructure/Persistence/Migrations/*AddKeyRevealedAt*` (new, generated) | `keys.revealed_at` column |

---

### Task 1: Domain — `OrderStatus.KeysAssigned` and `Order.MarkDelivered`

**Files:**
- Modify: `src/Maxkeys.Domain/Orders/OrderStatus.cs`
- Modify: `src/Maxkeys.Domain/Orders/Order.cs:168-209` (`AttachKey`)
- Test: `tests/Maxkeys.Domain.Tests/Orders/OrderTests.cs`

**Interfaces:**
- Produces: `OrderStatus.KeysAssigned`; `Order.MarkDelivered(DateTimeOffset now)` — throws `DomainConflictException` unless `Status == KeysAssigned`, else sets `Status = Delivered`, `DeliveredAt = now`.
- `Order.AttachKey`'s return value keeps its meaning ("did this call complete every item"), but completion now sets `Status = KeysAssigned` and leaves `DeliveredAt` `null`.

- [ ] **Step 1: Write the failing/changed tests**

In `tests/Maxkeys.Domain.Tests/Orders/OrderTests.cs`, replace the last test method (`AttachKey_AllOrNothing_DeliversOnlyWhenEveryItemComplete`) with:

```csharp
    [Fact]
    public void AttachKey_AllOrNothing_ReachesKeysAssignedOnlyWhenEveryItemComplete()
    {
        var order = CreateAwaitingFulfillmentOrder();
        var qty2Item = order.Items.Single(i => i.Quantity == 2);
        var qty1Item = order.Items.Single(i => i.Quantity == 1);

        Assert.False(order.AttachKey(qty2Item.Id, AvailableKey(qty2Item.ProductVariantId), Now.AddMinutes(3)));
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
        Assert.Null(order.DeliveredAt);

        Assert.False(order.AttachKey(qty1Item.Id, AvailableKey(qty1Item.ProductVariantId), Now.AddMinutes(4)));
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
        Assert.Null(order.DeliveredAt);

        Assert.True(order.AttachKey(qty2Item.Id, AvailableKey(qty2Item.ProductVariantId), Now.AddMinutes(5)));
        Assert.Equal(OrderStatus.KeysAssigned, order.Status);
        Assert.Null(order.DeliveredAt);
    }

    [Fact]
    public void MarkDelivered_WhenKeysAssigned_SetsDeliveredStatusAndTimestamp()
    {
        var order = CreateAwaitingFulfillmentOrder();
        var qty2Item = order.Items.Single(i => i.Quantity == 2);
        var qty1Item = order.Items.Single(i => i.Quantity == 1);
        order.AttachKey(qty2Item.Id, AvailableKey(qty2Item.ProductVariantId), Now.AddMinutes(3));
        order.AttachKey(qty1Item.Id, AvailableKey(qty1Item.ProductVariantId), Now.AddMinutes(4));
        order.AttachKey(qty2Item.Id, AvailableKey(qty2Item.ProductVariantId), Now.AddMinutes(5));
        Assert.Equal(OrderStatus.KeysAssigned, order.Status);

        order.MarkDelivered(Now.AddMinutes(6));

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(Now.AddMinutes(6), order.DeliveredAt);
    }

    [Fact]
    public void MarkDelivered_WhenNotKeysAssigned_Throws()
    {
        var order = CreateAwaitingFulfillmentOrder();
        Assert.Throws<DomainConflictException>(() => order.MarkDelivered(Now.AddMinutes(1)));
    }

    [Fact]
    public void MarkDelivered_WhenAlreadyDelivered_Throws()
    {
        var order = CreateAwaitingFulfillmentOrder();
        var qty2Item = order.Items.Single(i => i.Quantity == 2);
        var qty1Item = order.Items.Single(i => i.Quantity == 1);
        order.AttachKey(qty2Item.Id, AvailableKey(qty2Item.ProductVariantId), Now.AddMinutes(3));
        order.AttachKey(qty1Item.Id, AvailableKey(qty1Item.ProductVariantId), Now.AddMinutes(4));
        order.AttachKey(qty2Item.Id, AvailableKey(qty2Item.ProductVariantId), Now.AddMinutes(5));
        order.MarkDelivered(Now.AddMinutes(6));

        Assert.Throws<DomainConflictException>(() => order.MarkDelivered(Now.AddMinutes(7)));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Domain.Tests`
Expected: FAIL — `OrderStatus.KeysAssigned` and `Order.MarkDelivered` don't exist yet (compile error).

- [ ] **Step 3: Implement**

In `src/Maxkeys.Domain/Orders/OrderStatus.cs`, replace the whole file with:

```csharp
namespace Maxkeys.Domain.Orders;

/// <summary>Order lifecycle status (design section 4.2). `KeysAssigned` and `Delivered` are derived/set by `Order.AttachKey`/`Order.MarkDelivered`.</summary>
public enum OrderStatus
{
    Pending,
    Paid,
    AwaitingFulfillment,
    KeysAssigned,
    Delivered,
    Cancelled
}
```

In `src/Maxkeys.Domain/Orders/Order.cs`, replace the `AttachKey` method's doc comment and completion branch:

```csharp
    /// <summary>
    /// Assigns <paramref name="key"/> to <paramref name="orderItemId"/> (fulfillment
    /// spec: Key Attachment). Once every item is complete the order transitions to
    /// <see cref="OrderStatus.KeysAssigned"/> and this method returns <c>true</c>
    /// (fulfillment spec: All-or-Nothing Delivery Derivation; admin-key-delivery-gate
    /// spec: decision 1 — MODIFIED, no longer reaches <see cref="OrderStatus.Delivered"/>
    /// on its own). An admin must call <see cref="MarkDelivered"/> to release the
    /// order to its buyer. Otherwise returns <c>false</c>.
    /// </summary>
    public bool AttachKey(Guid orderItemId, Key key, DateTimeOffset now)
    {
        if (Status != OrderStatus.AwaitingFulfillment)
        {
            throw new DomainConflictException("Cannot attach a key unless the order is awaiting fulfillment.");
        }

        var item = _items.FirstOrDefault(i => i.Id == orderItemId);
        if (item is null)
        {
            throw new DomainException("Order item does not belong to this order.");
        }

        if (key.ProductVariantId != item.ProductVariantId)
        {
            throw new DomainException("Key product variant does not match the order item.");
        }

        if (item.IsComplete)
        {
            throw new DomainConflictException("Order item already has all required keys assigned.");
        }

        key.AssignTo(item.Id, now);
        item.AddKey(key);
        UpdatedAt = now;

        if (!_items.All(i => i.IsComplete))
        {
            return false;
        }

        Status = OrderStatus.KeysAssigned;
        return true;
    }

    /// <summary>
    /// Admin action that releases an order's keys to its buyer (admin-key-delivery-gate
    /// spec: decision 1/3, "Entregar"). Valid only from <see cref="OrderStatus.KeysAssigned"/>.
    /// </summary>
    public void MarkDelivered(DateTimeOffset now)
    {
        if (Status != OrderStatus.KeysAssigned)
        {
            throw new DomainConflictException("Cannot deliver an order unless every item has its keys assigned.");
        }

        Status = OrderStatus.Delivered;
        DeliveredAt = now;
        UpdatedAt = now;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Domain.Tests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Domain/Orders/OrderStatus.cs src/Maxkeys.Domain/Orders/Order.cs tests/Maxkeys.Domain.Tests/Orders/OrderTests.cs
git commit -m "feat(vault): add KeysAssigned order status and admin MarkDelivered gate"
```

---

### Task 2: Domain — `KeyStatus.Revealed` and `Key.Reveal`

**Files:**
- Modify: `src/Maxkeys.Domain/Keys/KeyStatus.cs`
- Modify: `src/Maxkeys.Domain/Keys/Key.cs`
- Test: `tests/Maxkeys.Domain.Tests/Keys/KeyTests.cs`

**Interfaces:**
- Produces: `KeyStatus.Revealed`; `Key.RevealedAt` (`DateTimeOffset?`); `Key.Reveal(DateTimeOffset now)` — throws `DomainConflictException` unless `Status == Assigned`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Maxkeys.Domain.Tests/Keys/KeyTests.cs`, inside the `KeyTests` class, after `AssignTo_WhenAlreadyAssigned_Throws`:

```csharp

    [Fact]
    public void Reveal_WhenAssigned_SetsRevealedAndTimestamp()
    {
        var key = new Key(Guid.NewGuid(), Code, 1, "admin@example.com", Now);
        key.AssignTo(Guid.NewGuid(), Now.AddMinutes(1));

        key.Reveal(Now.AddMinutes(2));

        Assert.Equal(KeyStatus.Revealed, key.Status);
        Assert.Equal(Now.AddMinutes(2), key.RevealedAt);
    }

    [Fact]
    public void Reveal_WhenAvailable_Throws()
    {
        var key = new Key(Guid.NewGuid(), Code, 1, "admin@example.com", Now);
        Assert.Throws<DomainConflictException>(() => key.Reveal(Now.AddMinutes(1)));
    }

    [Fact]
    public void Reveal_WhenAlreadyRevealed_Throws()
    {
        var key = new Key(Guid.NewGuid(), Code, 1, "admin@example.com", Now);
        key.AssignTo(Guid.NewGuid(), Now.AddMinutes(1));
        key.Reveal(Now.AddMinutes(2));
        Assert.Throws<DomainConflictException>(() => key.Reveal(Now.AddMinutes(3)));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Domain.Tests`
Expected: FAIL — `KeyStatus.Revealed`, `Key.RevealedAt`, `Key.Reveal` don't exist yet.

- [ ] **Step 3: Implement**

`src/Maxkeys.Domain/Keys/KeyStatus.cs`, full file:

```csharp
namespace Maxkeys.Domain.Keys;

/// <summary>Key lifecycle status (design section 4.2; admin-key-delivery-gate spec: decision 2 adds Revealed).</summary>
public enum KeyStatus
{
    Available,
    Assigned,
    Revealed
}
```

In `src/Maxkeys.Domain/Keys/Key.cs`, add the property next to `AssignedAt` and the method after `AssignTo`:

```csharp
    public DateTimeOffset? AssignedAt { get; private set; }
    public DateTimeOffset? RevealedAt { get; private set; }
```

```csharp
    /// <summary>Reveals this key to its buyer (admin-key-delivery-gate spec: decision 2, buyer "revelar key"). Valid only from Assigned.</summary>
    public void Reveal(DateTimeOffset now)
    {
        if (Status != KeyStatus.Assigned)
        {
            throw new DomainConflictException("Cannot reveal a key that is not assigned.");
        }

        Status = KeyStatus.Revealed;
        RevealedAt = now;
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Domain.Tests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Domain/Keys/KeyStatus.cs src/Maxkeys.Domain/Keys/Key.cs tests/Maxkeys.Domain.Tests/Keys/KeyTests.cs
git commit -m "feat(vault): add Revealed key status and Key.Reveal"
```

---

### Task 3: Application — extract `AssignVaultKeysToOrder`

**Files:**
- Create: `src/Maxkeys.Application/Fulfillment/AssignVaultKeysToOrder.cs`
- Test: `tests/Maxkeys.Application.Tests/Fulfillment/AssignVaultKeysToOrderTests.cs` (new)

**Interfaces:**
- Consumes: `IAppDbContext` (`Maxkeys.Application.Persistence`), `Order.AttachKey` (Task 1), `Product.VaultEnabled`/`LoadVaultKeys` (existing vault spec).
- Produces: `AssignVaultKeysToOrder(IAppDbContext db)`, `Task<AssignVaultKeysToOrderResult?> ExecuteAsync(Guid orderId, CancellationToken)`; `AssignVaultKeysToOrderResult(OrderStatus OrderStatus, bool AllItemsComplete, IReadOnlyList<AssignVaultKeysToOrderResultItem> Items)`; `AssignVaultKeysToOrderResultItem(Guid ItemId, int Quantity, int AssignedKeys)`. Used by Task 4 (`OrderApprovedHandler`) and Task 11 (admin `assign-keys` endpoint).

- [ ] **Step 1: Write the failing tests**

Create `tests/Maxkeys.Application.Tests/Fulfillment/AssignVaultKeysToOrderTests.cs`:

```csharp
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
        var availableRemaining = await verifyContext.Keys.CountAsync(k => k.Status == KeyStatus.Available);
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

    private async Task<Guid> SeedAwaitingFulfillmentOrderAsync(Guid variantId, int quantity)
    {
        await using var context = _fixture.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(variantId, "Vault Product", "Standard", 1_000m, quantity)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter AssignVaultKeysToOrderTests`
Expected: FAIL — `AssignVaultKeysToOrder` doesn't exist yet.

- [ ] **Step 3: Implement**

Create `src/Maxkeys.Application/Fulfillment/AssignVaultKeysToOrder.cs`:

```csharp
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Attempts to auto-assign vault stock to every incomplete item of one order
/// whose product has vault auto-fulfillment enabled (admin-key-delivery-gate
/// spec: decision 3; vault spec: Vault Auto-Assignment On Approval). Per item,
/// all-or-nothing: if available stock is less than the item's remaining
/// quantity, that item is skipped entirely. Called both automatically by
/// <see cref="Outbox.OrderApprovedHandler"/> right after payment approval and
/// on demand by the admin "Asignar" action for orders that didn't have full
/// stock at approval time. A no-op (current state returned unchanged) for any
/// order not <see cref="OrderStatus.AwaitingFulfillment"/> — nothing left to
/// assign. Returns <see langword="null"/> for an unknown order id.
/// </summary>
public sealed class AssignVaultKeysToOrder
{
    private readonly IAppDbContext _db;

    public AssignVaultKeysToOrder(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<AssignVaultKeysToOrderResult?> ExecuteAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (order.Status != OrderStatus.AwaitingFulfillment)
        {
            return ToResult(order);
        }

        try
        {
            await AssignAsync(order, now, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            DetachTrackedState();

            // Same reload-and-retry-once pattern as AttachKeyToOrderItem: a concurrent auto-assign
            // for another order may have consumed the same vault keys between our read and write.
            order = await LoadOrderAsync(orderId, cancellationToken)
                ?? throw new DomainConflictException("Order no longer exists.");

            if (order.Status != OrderStatus.AwaitingFulfillment)
            {
                return ToResult(order);
            }

            await AssignAsync(order, now, cancellationToken);
        }

        return ToResult(order);
    }

    private async Task AssignAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var incompleteItems = order.Items.Where(i => !i.IsComplete).ToList();
        if (incompleteItems.Count == 0)
        {
            return;
        }

        var variantIds = incompleteItems.Select(i => i.ProductVariantId).Distinct().ToList();
        var variantProductIds = await _db.ProductVariants
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new { v.Id, v.ProductId })
            .ToListAsync(cancellationToken);
        var productIdByVariant = variantProductIds.ToDictionary(v => v.Id, v => v.ProductId);

        var productIds = productIdByVariant.Values.Distinct().ToList();
        var vaultEnabledByProduct = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.VaultEnabled })
            .ToDictionaryAsync(p => p.Id, p => p.VaultEnabled, cancellationToken);

        foreach (var item in incompleteItems)
        {
            if (!productIdByVariant.TryGetValue(item.ProductVariantId, out var productId) ||
                !vaultEnabledByProduct.TryGetValue(productId, out var vaultEnabled) ||
                !vaultEnabled)
            {
                continue;
            }

            var remaining = item.Quantity - item.Keys.Count(k => k.Status == KeyStatus.Assigned);
            if (remaining <= 0)
            {
                continue;
            }

            var availableKeys = await _db.Keys
                .Where(k => k.ProductVariantId == item.ProductVariantId && k.Status == KeyStatus.Available)
                .OrderBy(k => k.CreatedAt)
                .Take(remaining)
                .ToListAsync(cancellationToken);

            if (availableKeys.Count < remaining)
            {
                continue; // all-or-nothing: leave this item for the manual flow
            }

            foreach (var key in availableKeys)
            {
                order.AttachKey(item.Id, key, now);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    /// <summary>
    /// Detaches only what THIS call's failed attempt added to the change tracker —
    /// safe whether called from a dedicated per-request context (admin "Asignar"
    /// endpoint) or from the outbox processor's shared batch context
    /// (<c>OrderApprovedHandler</c>): a blind <c>ChangeTracker.Clear()</c> would
    /// silently drop other claimed <c>OutboxEvent</c> rows' pending status
    /// mutations in the shared-context case (see
    /// <c>OrderApprovedHandlerTests.Concurrency_retry_does_not_lose_a_sibling_outbox_events_status_update</c>).
    /// </summary>
    private void DetachTrackedState()
    {
        if (_db is not DbContext dbContext)
        {
            return;
        }

        foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is OutboxEvent && entry.State != EntityState.Added)
            {
                continue;
            }

            entry.State = EntityState.Detached;
        }
    }

    private static AssignVaultKeysToOrderResult ToResult(Order order) => new(
        order.Status,
        order.Items.All(i => i.IsComplete),
        order.Items
            .Select(i => new AssignVaultKeysToOrderResultItem(i.Id, i.Quantity, i.Keys.Count(k => k.Status == KeyStatus.Assigned)))
            .ToList());
}

/// <summary>Per-item progress after an assign attempt (admin-key-delivery-gate spec: decision 3).</summary>
public sealed record AssignVaultKeysToOrderResultItem(Guid ItemId, int Quantity, int AssignedKeys);

/// <summary>Order status and per-item progress returned by <see cref="AssignVaultKeysToOrder"/>.</summary>
public sealed record AssignVaultKeysToOrderResult(OrderStatus OrderStatus, bool AllItemsComplete, IReadOnlyList<AssignVaultKeysToOrderResultItem> Items);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter AssignVaultKeysToOrderTests`
Expected: PASS (6 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Fulfillment/AssignVaultKeysToOrder.cs tests/Maxkeys.Application.Tests/Fulfillment/AssignVaultKeysToOrderTests.cs
git commit -m "feat(vault): extract AssignVaultKeysToOrder as a standalone, admin-triggerable use case"
```

---

### Task 4: Application — wire `OrderApprovedHandler` to `AssignVaultKeysToOrder`

**Files:**
- Modify: `src/Maxkeys.Application/Outbox/OrderApprovedHandler.cs`
- Test: `tests/Maxkeys.Application.Tests/Outbox/OrderApprovedHandlerTests.cs`

**Interfaces:**
- Consumes: `AssignVaultKeysToOrder` (Task 3).

- [ ] **Step 1: Write the failing/changed tests**

In `tests/Maxkeys.Application.Tests/Outbox/OrderApprovedHandlerTests.cs`:

Replace the `CreateHandler` helper:

```csharp
    private static OrderApprovedHandler CreateHandler(AppDbContext context, RecordingEmailSender emailSender)
    {
        var options = Options.Create(new EmailOptions { OperatorTo = OperatorAddress });
        return new OrderApprovedHandler(context, new AssignVaultKeysToOrder(context), emailSender, options, NullLogger<OrderApprovedHandler>.Instance);
    }
```

Replace `Vault_enabled_with_full_stock_auto_delivers_and_skips_the_operator_email` with:

```csharp
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
```

`Vault_enabled_with_partial_stock_skips_that_item_and_still_sends_the_operator_email` and `Vault_disabled_never_auto_assigns_even_with_stock_available` stay unchanged — no edits needed there.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter OrderApprovedHandlerTests`
Expected: FAIL — `OrderApprovedHandler` constructor doesn't accept an `AssignVaultKeysToOrder` param yet; `deliveredEvents`/`OrderStatus.Delivered` assertions from the old test no longer match.

- [ ] **Step 3: Implement**

Replace `src/Maxkeys.Application/Outbox/OrderApprovedHandler.cs` in full:

```csharp
using System.Text.Json;
using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Notifications;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Outbox;

/// <summary>
/// Handles <see cref="OutboxEventTypes.OrderApproved"/> (outbox-processing
/// spec: OrderApproved Handler; design section 6c; admin-key-delivery-gate
/// spec: decision 3 — MODIFIED). Transitions the order
/// <see cref="OrderStatus.Paid"/> → <see cref="OrderStatus.AwaitingFulfillment"/>,
/// then delegates vault auto-assignment to <see cref="AssignVaultKeysToOrder"/>
/// (shared with the admin "Asignar" action). Always emails the operator
/// afterwards — this handler never reaches <see cref="OrderStatus.Delivered"/>
/// on its own any more, an admin always reviews before delivery. Idempotent:
/// an order already past <see cref="OrderStatus.Paid"/> is a logged no-op with
/// no email; an order that does not exist is a bug in the producer, so this
/// throws to let the outbox processor retry/dead-letter rather than silently
/// drop the event.
/// </summary>
public sealed class OrderApprovedHandler : IOutboxHandler
{
    private readonly IAppDbContext _db;
    private readonly AssignVaultKeysToOrder _assignVaultKeysToOrder;
    private readonly IEmailSender _emailSender;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly ILogger<OrderApprovedHandler> _logger;

    public string EventType => OutboxEventTypes.OrderApproved;

    public OrderApprovedHandler(
        IAppDbContext db,
        AssignVaultKeysToOrder assignVaultKeysToOrder,
        IEmailSender emailSender,
        IOptions<EmailOptions> emailOptions,
        ILogger<OrderApprovedHandler> logger)
    {
        _db = db;
        _assignVaultKeysToOrder = assignVaultKeysToOrder;
        _emailSender = emailSender;
        _emailOptions = emailOptions;
        _logger = logger;
    }

    public async Task HandleAsync(OutboxEvent evt, CancellationToken cancellationToken)
    {
        var orderId = ParseOrderId(evt.Payload);
        var now = DateTimeOffset.UtcNow;

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new InvalidOperationException($"OrderApproved event references unknown order {orderId}.");
        }

        if (IsPastApproval(order.Status))
        {
            _logger.LogInformation(
                "Order {OrderId} is already {Status}; OrderApproved handling is a no-op.", orderId, order.Status);
            return;
        }

        try
        {
            order.MarkAwaitingFulfillment(now);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            DetachHandlerState();

            order = await LoadOrderAsync(orderId, cancellationToken)
                ?? throw new InvalidOperationException($"OrderApproved event references unknown order {orderId}.");

            if (IsPastApproval(order.Status))
            {
                _logger.LogInformation(
                    "Order {OrderId} is already {Status} after a concurrent update; OrderApproved handling is a no-op.",
                    orderId, order.Status);
                return;
            }

            order.MarkAwaitingFulfillment(now);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var assignResult = await _assignVaultKeysToOrder.ExecuteAsync(orderId, cancellationToken);
        _logger.LogInformation(
            "Vault auto-assign for order {OrderId}: all items complete = {AllComplete}.",
            orderId, assignResult?.AllItemsComplete ?? false);

        var (subject, textBody) = EmailTemplates.OperatorOrderAwaitingFulfillment(order);
        await _emailSender.SendAsync(new EmailMessage(_emailOptions.Value.OperatorTo, subject, textBody), cancellationToken);
    }

    private static bool IsPastApproval(OrderStatus status) =>
        status is OrderStatus.AwaitingFulfillment or OrderStatus.KeysAssigned or OrderStatus.Delivered;

    private Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    /// <summary>
    /// Detaches only what THIS handler's failed attempt added to the change
    /// tracker, on a <see cref="DbUpdateConcurrencyException"/> retry. Unlike
    /// <c>AttachKeyToOrderItem</c>'s <c>ClearTracker</c> (safe there because it
    /// runs in a single-purpose per-HTTP-request scope), this handler runs
    /// inside <c>OutboxProcessor.ProcessOnceAsync</c>'s shared batch
    /// <see cref="DbContext"/> (design section 6; <c>OutboxProcessor.cs</c>),
    /// where other claimed <see cref="OutboxEvent"/> rows are also tracked and
    /// still need their <c>MarkProcessed</c>/<c>MarkFailedAttempt</c> mutations
    /// to persist later in the same pass. A full <c>ChangeTracker.Clear()</c>
    /// would detach those too, silently dropping their status updates.
    /// </summary>
    private void DetachHandlerState()
    {
        if (_db is not DbContext dbContext)
        {
            return;
        }

        foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is OutboxEvent && entry.State != EntityState.Added)
            {
                continue;
            }

            entry.State = EntityState.Detached;
        }
    }

    private static Guid ParseOrderId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var orderIdText = document.RootElement.GetProperty("orderId").GetString();
        return Guid.Parse(orderIdText!);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter OrderApprovedHandlerTests`
Expected: PASS (all 7 tests, including the untouched concurrency regression test)

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Outbox/OrderApprovedHandler.cs tests/Maxkeys.Application.Tests/Outbox/OrderApprovedHandlerTests.cs
git commit -m "refactor(vault): delegate OrderApprovedHandler's auto-assign to AssignVaultKeysToOrder"
```

---

### Task 5: Application — `AttachKeyToOrderItem` stops enqueueing `OrderDelivered`

**Files:**
- Modify: `src/Maxkeys.Application/Fulfillment/AttachKeyToOrderItem.cs`
- Test: `tests/Maxkeys.Application.Tests/Fulfillment/AttachKeyToOrderItemTests.cs`

- [ ] **Step 1: Write the failing/changed tests**

In `tests/Maxkeys.Application.Tests/Fulfillment/AttachKeyToOrderItemTests.cs`:

Remove `using Maxkeys.Domain.Outbox;` from the top (no longer used).

In `Concurrent_attach_on_the_last_slot_yields_exactly_one_winner`, change the final assertion:

```csharp
        await using var readContext = _fixture.CreateContext();
        var order = await readContext.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.KeysAssigned, order.Status);
```

Replace `Last_key_completes_delivery_and_inserts_exactly_one_order_delivered_event` in full with:

```csharp
    [Fact]
    public async Task Last_key_completes_delivery_and_reaches_keys_assigned()
    {
        var (orderId, itemId, _) = await SeedAwaitingFulfillmentOrderAsync(completeQuantity: null, incompleteQuantity: 1);

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var result = await sut.ExecuteAsync(orderId, itemId, "FINAL-CODE", "admin@maxkeys.test");

        Assert.NotNull(result);
        Assert.Equal(OrderStatus.KeysAssigned, result!.OrderStatus);

        var order = await context.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.KeysAssigned, order.Status);
        Assert.Null(order.DeliveredAt);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter AttachKeyToOrderItemTests`
Expected: FAIL — assertions expect `KeysAssigned`, source still produces `Delivered` and an `OrderDelivered` event.

- [ ] **Step 3: Implement**

In `src/Maxkeys.Application/Fulfillment/AttachKeyToOrderItem.cs`:

Replace the class doc comment:

```csharp
/// <summary>
/// Attaches exactly one key code to an order item (fulfillment spec: Key
/// Attachment, All-or-Nothing Delivery Derivation, Key Encryption at Rest;
/// design section 6d; admin-key-delivery-gate spec: decision 1 — MODIFIED).
/// Encrypts the plaintext code immediately, then persists the key and the
/// order's derived status in a single <c>SaveChangesAsync</c>. Completing
/// every item stops at <see cref="OrderStatus.KeysAssigned"/> — an admin must
/// separately call <c>DeliverOrder</c> to release the order to its buyer, so
/// no outbox event is inserted here any more. Returns <see langword="null"/>
/// for an unknown order so the API layer can map it to 404, matching
/// <c>GetOrderStatus</c>/<c>GetProductBySlug</c>.
/// </summary>
```

Remove the `using Maxkeys.Domain.Outbox;` line from the top of the file.

Replace the body of `AttachAsync`:

```csharp
    private async Task<AttachKeyToOrderItemResult> AttachAsync(
        Order order, Guid orderItemId, byte[] blob, short version, string loadedBy, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var item = order.Items.FirstOrDefault(i => i.Id == orderItemId)
            ?? throw new DomainException("Order item does not belong to this order.");

        var key = new Key(item.ProductVariantId, blob, version, loadedBy, now);

        // Key.Id is client-generated (Entity base class), so EF Core's navigation-fixup change
        // detection cannot tell this is a brand-new row once it is only reachable through the
        // already-tracked item's Keys collection — it must be added to the DbSet explicitly, the
        // same way OutboxEvent rows are (ProcessPaymentNotification), or EF issues an UPDATE
        // instead of an INSERT and a spurious DbUpdateConcurrencyException follows.
        _db.Keys.Add(key);
        order.AttachKey(orderItemId, key, now);

        await _db.SaveChangesAsync(cancellationToken);

        return new AttachKeyToOrderItemResult(
            order.Status,
            order.Items
                .Select(i => new AttachKeyToOrderItemResultItem(i.Id, i.Quantity, i.Keys.Count(k => k.Status == KeyStatus.Assigned)))
                .ToList());
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter AttachKeyToOrderItemTests`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Fulfillment/AttachKeyToOrderItem.cs tests/Maxkeys.Application.Tests/Fulfillment/AttachKeyToOrderItemTests.cs
git commit -m "fix(vault): stop AttachKeyToOrderItem from auto-delivering, admin gate required"
```

---

### Task 6: Application — `DeliverOrder` (admin "Entregar")

**Files:**
- Create: `src/Maxkeys.Application/Fulfillment/DeliverOrder.cs`
- Test: `tests/Maxkeys.Application.Tests/Fulfillment/DeliverOrderTests.cs` (new)

**Interfaces:**
- Produces: `DeliverOrder(IAppDbContext db, ILogger<DeliverOrder> logger)`, `Task<OrderStatus?> ExecuteAsync(Guid orderId, string deliveredBy, CancellationToken)`. Used by Task 11 (admin `deliver` endpoint).

- [ ] **Step 1: Write the failing tests**

Create `tests/Maxkeys.Application.Tests/Fulfillment/DeliverOrderTests.cs`:

```csharp
using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>Covers <see cref="DeliverOrder"/> (admin-key-delivery-gate spec: decision 3, "Entregar").</summary>
[Collection(PostgresCollection.Name)]
public sealed class DeliverOrderTests
{
    private readonly PostgresFixture _fixture;

    public DeliverOrderTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task KeysAssigned_order_transitions_to_delivered()
    {
        var orderId = await SeedKeysAssignedOrderAsync();

        await using var context = _fixture.CreateContext();
        var sut = new DeliverOrder(context, NullLogger<DeliverOrder>.Instance);

        var status = await sut.ExecuteAsync(orderId, "admin@maxkeys.test");

        Assert.Equal(OrderStatus.Delivered, status);

        var order = await context.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveredAt);
    }

    [Fact]
    public async Task AwaitingFulfillment_order_throws_conflict()
    {
        var orderId = await SeedAwaitingFulfillmentOrderAsync();

        await using var context = _fixture.CreateContext();
        var sut = new DeliverOrder(context, NullLogger<DeliverOrder>.Instance);

        await Assert.ThrowsAsync<DomainConflictException>(() => sut.ExecuteAsync(orderId, "admin@maxkeys.test"));
    }

    [Fact]
    public async Task Unknown_order_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var sut = new DeliverOrder(context, NullLogger<DeliverOrder>.Instance);

        var status = await sut.ExecuteAsync(Guid.NewGuid(), "admin@maxkeys.test");

        Assert.Null(status);
    }

    private async Task<Guid> SeedKeysAssignedOrderAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = _fixture.CreateContext();
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, new byte[] { 1, 2, 3 }, 1, "seed-admin", now);
        context.Keys.Add(key);
        order.AttachKey(item.Id, key, now);
        await context.SaveChangesAsync();

        return order.Id;
    }

    private async Task<Guid> SeedAwaitingFulfillmentOrderAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = _fixture.CreateContext();
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter DeliverOrderTests`
Expected: FAIL — `DeliverOrder` doesn't exist yet.

- [ ] **Step 3: Implement**

Create `src/Maxkeys.Application/Fulfillment/DeliverOrder.cs`:

```csharp
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Admin action that releases an order's already-assigned keys to its buyer
/// (admin-key-delivery-gate spec: decision 3, "Entregar"). Valid only from
/// <see cref="OrderStatus.KeysAssigned"/> — <see cref="Order.MarkDelivered"/>
/// throws <see cref="Domain.Common.DomainConflictException"/> (409) otherwise,
/// e.g. an item still lacks stock or the order was already delivered. Returns
/// <see langword="null"/> for an unknown order so the API layer maps it to
/// 404. No outbox/email side effect — delivery only opens buyer visibility in
/// <c>/account/orders</c> (decision 4: no automatic email for now).
/// </summary>
public sealed class DeliverOrder
{
    private readonly IAppDbContext _db;
    private readonly ILogger<DeliverOrder> _logger;

    public DeliverOrder(IAppDbContext db, ILogger<DeliverOrder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<OrderStatus?> ExecuteAsync(Guid orderId, string deliveredBy, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        order.MarkDelivered(now);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order {OrderId} marked Delivered by admin {AdminSub}.", orderId, deliveredBy);

        return order.Status;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter DeliverOrderTests`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Fulfillment/DeliverOrder.cs tests/Maxkeys.Application.Tests/Fulfillment/DeliverOrderTests.cs
git commit -m "feat(vault): add DeliverOrder admin action"
```

---

### Task 7: Application — `RevealOrderItemKeys` (buyer "revelar key")

**Files:**
- Create: `src/Maxkeys.Application/Orders/RevealOrderItemKeys.cs`
- Test: `tests/Maxkeys.Application.Tests/Orders/RevealOrderItemKeysTests.cs` (new)

**Interfaces:**
- Consumes: `KeyCipher` (`Maxkeys.Application.Security`).
- Produces: `RevealOrderItemKeys(IAppDbContext db, KeyCipher keyCipher, ILogger<RevealOrderItemKeys> logger)`, `Task<IReadOnlyList<string>?> ExecuteAsync(Guid orderId, Guid itemId, Guid userId, CancellationToken)`. Used by Task 12 (buyer reveal endpoint).

- [ ] **Step 1: Write the failing tests**

Create `tests/Maxkeys.Application.Tests/Orders/RevealOrderItemKeysTests.cs`:

```csharp
using Maxkeys.Application.Orders;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Orders;

/// <summary>Covers <see cref="RevealOrderItemKeys"/> (admin-key-delivery-gate spec: decision 2/4, buyer "revelar key").</summary>
[Collection(PostgresCollection.Name)]
public sealed class RevealOrderItemKeysTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public RevealOrderItemKeysTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Delivered_order_reveals_every_assigned_key_for_that_item()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(userId, quantity: 2, codes: ["CODE-A", "CODE-B"]);

        await using var context = _fixture.CreateContext();
        var codes = await CreateSut(context).ExecuteAsync(orderId, itemId, userId);

        Assert.NotNull(codes);
        Assert.Equal(new[] { "CODE-A", "CODE-B" }, codes!.OrderBy(c => c));

        var item = await context.Orders.Include(o => o.Items).ThenInclude(i => i.Keys)
            .SelectMany(o => o.Items).SingleAsync(i => i.Id == itemId);
        Assert.All(item.Keys, k => Assert.Equal(KeyStatus.Revealed, k.Status));
    }

    [Fact]
    public async Task Revealing_twice_returns_the_same_codes_and_does_not_re_mutate()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(userId, quantity: 1, codes: ["ONLY-CODE"]);

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var first = await sut.ExecuteAsync(orderId, itemId, userId);
        var second = await sut.ExecuteAsync(orderId, itemId, userId);

        Assert.Equal(["ONLY-CODE"], first);
        Assert.Equal(["ONLY-CODE"], second);
    }

    [Fact]
    public async Task Non_owner_returns_null()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(owner, quantity: 1, codes: ["CODE"]);

        await using var context = _fixture.CreateContext();
        var codes = await CreateSut(context).ExecuteAsync(orderId, itemId, stranger);

        Assert.Null(codes);
    }

    [Fact]
    public async Task Unknown_order_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var codes = await CreateSut(context).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(codes);
    }

    [Fact]
    public async Task Order_not_yet_delivered_throws_conflict()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedKeysAssignedOrderAsync(userId);

        await using var context = _fixture.CreateContext();
        await Assert.ThrowsAsync<DomainConflictException>(() => CreateSut(context).ExecuteAsync(orderId, itemId, userId));
    }

    [Fact]
    public async Task Item_from_another_order_throws()
    {
        var userId = Guid.NewGuid();
        var (orderId, _) = await SeedDeliveredOrderAsync(userId, quantity: 1, codes: ["CODE"]);
        var (_, otherItemId) = await SeedDeliveredOrderAsync(userId, quantity: 1, codes: ["OTHER"]);

        await using var context = _fixture.CreateContext();
        await Assert.ThrowsAsync<DomainException>(() => CreateSut(context).ExecuteAsync(orderId, otherItemId, userId));
    }

    private static RevealOrderItemKeys CreateSut(Infrastructure.Persistence.AppDbContext context) =>
        new(context, new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 })), NullLogger<RevealOrderItemKeys>.Instance);

    private async Task<(Guid OrderId, Guid ItemId)> SeedKeysAssignedOrderAsync(Guid userId, int quantity = 1, string[]? codes = null)
    {
        codes ??= ["SEED-CODE"];
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        await using var context = _fixture.CreateContext();
        var order = Order.Create(userId, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, quantity)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        foreach (var code in codes)
        {
            var (blob, version) = cipher.Encrypt(code);
            var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
            context.Keys.Add(key);
            order.AttachKey(item.Id, key, now);
        }
        await context.SaveChangesAsync();

        return (order.Id, item.Id);
    }

    private async Task<(Guid OrderId, Guid ItemId)> SeedDeliveredOrderAsync(Guid userId, int quantity, string[] codes)
    {
        var (orderId, itemId) = await SeedKeysAssignedOrderAsync(userId, quantity, codes);

        await using var context = _fixture.CreateContext();
        var order = await context.Orders.SingleAsync(o => o.Id == orderId);
        order.MarkDelivered(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        return (orderId, itemId);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter RevealOrderItemKeysTests`
Expected: FAIL — `RevealOrderItemKeys` doesn't exist yet.

- [ ] **Step 3: Implement**

Create `src/Maxkeys.Application/Orders/RevealOrderItemKeys.cs`:

```csharp
using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maxkeys.Application.Orders;

/// <summary>
/// Buyer action that reveals every key assigned to one order item, once
/// (admin-key-delivery-gate spec: decision 2/4, "revelar key"). Same
/// ownership-hiding pattern as <see cref="GetMyOrder"/>: returns
/// <see langword="null"/> both for an unknown order and one owned by someone
/// else (ADR-14). Only allowed once the order is <see cref="OrderStatus.Delivered"/>.
/// Idempotent: a key already <see cref="KeyStatus.Revealed"/> is returned
/// again without re-mutating, so a buyer can safely revisit and still see the
/// code.
/// </summary>
public sealed class RevealOrderItemKeys
{
    private readonly IAppDbContext _db;
    private readonly KeyCipher _keyCipher;
    private readonly ILogger<RevealOrderItemKeys> _logger;

    public RevealOrderItemKeys(IAppDbContext db, KeyCipher keyCipher, ILogger<RevealOrderItemKeys> logger)
    {
        _db = db;
        _keyCipher = keyCipher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>?> ExecuteAsync(Guid orderId, Guid itemId, Guid userId, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null || order.UserId != userId)
        {
            return null;
        }

        if (order.Status != OrderStatus.Delivered)
        {
            throw new DomainConflictException("Cannot reveal keys unless the order is Delivered.");
        }

        var item = order.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new DomainException("Order item does not belong to this order.");

        var now = DateTimeOffset.UtcNow;
        var keys = item.Keys.Where(k => k.Status is KeyStatus.Assigned or KeyStatus.Revealed).ToList();
        var revealedCount = 0;
        foreach (var key in keys.Where(k => k.Status == KeyStatus.Assigned))
        {
            key.Reveal(now);
            revealedCount++;
        }

        if (revealedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Buyer {UserId} revealed {Count} key(s) for order {OrderId} item {ItemId}.", userId, revealedCount, orderId, itemId);
        }

        return keys.Select(k => _keyCipher.Decrypt(k.EncryptedCode, k.KeyVersion)).ToList();
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter RevealOrderItemKeysTests`
Expected: PASS (6 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Orders/RevealOrderItemKeys.cs tests/Maxkeys.Application.Tests/Orders/RevealOrderItemKeysTests.cs
git commit -m "feat(vault): add RevealOrderItemKeys buyer action"
```

---

### Task 8: Application — `GetMyOrder` only shows revealed codes

**Files:**
- Modify: `src/Maxkeys.Application/Orders/GetMyOrder.cs`
- Modify: `src/Maxkeys.Application/Orders/OrdersDtos.cs`
- Test: `tests/Maxkeys.Application.Tests/Orders/GetMyOrdersTests.cs`

**Interfaces:**
- Produces: `MyOrderItemDetail(string ProductName, string VariantName, decimal UnitPrice, int Quantity, IReadOnlyList<string> Keys, bool Revealable)` — `Keys` is now always a list (never `null`), populated only with already-`Revealed` codes.

- [ ] **Step 1: Write the failing/changed tests**

In `tests/Maxkeys.Application.Tests/Orders/GetMyOrdersTests.cs`:

Replace `GetMyOrder_hides_keys_when_not_delivered_and_reveals_them_when_delivered` with:

```csharp
    [Fact]
    public async Task GetMyOrder_keeps_keys_hidden_until_explicitly_revealed_even_after_delivered()
    {
        var userId = Guid.NewGuid();

        await using var seedContext = _fixture.CreateContext();
        var awaitingOrderId = await SeedOrderAsync(seedContext, userId, deliver: false);
        var deliveredOrderId = await SeedOrderAsync(seedContext, userId, deliver: true);

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var awaiting = await sut.ExecuteAsync(awaitingOrderId, userId);
        var delivered = await sut.ExecuteAsync(deliveredOrderId, userId);

        Assert.NotNull(awaiting);
        Assert.All(awaiting!.Items, item =>
        {
            Assert.Empty(item.Keys);
            Assert.False(item.Revealable);
        });

        Assert.NotNull(delivered);
        Assert.All(delivered!.Items, item =>
        {
            Assert.Empty(item.Keys); // assigned, not yet revealed
            Assert.True(item.Revealable);
        });
    }

    [Fact]
    public async Task GetMyOrder_shows_a_revealed_keys_code_on_later_calls()
    {
        var userId = Guid.NewGuid();

        await using var seedContext = _fixture.CreateContext();
        var orderId = await SeedOrderAsync(seedContext, userId, deliver: true);

        await using (var revealContext = _fixture.CreateContext())
        {
            var order = await revealContext.Orders.Include(o => o.Items).ThenInclude(i => i.Keys).SingleAsync(o => o.Id == orderId);
            var key = order.Items.Single().Keys.Single();
            key.Reveal(DateTimeOffset.UtcNow);
            await revealContext.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var result = await CreateSut(context).ExecuteAsync(orderId, userId);

        Assert.NotNull(result);
        var item = result!.Items.Single();
        Assert.Single(item.Keys);
        Assert.Equal("SEED-CODE", item.Keys[0]);
        Assert.False(item.Revealable); // nothing left to reveal
    }
```

Replace the `SeedOrderAsync` helper's `deliver` branch (the order must actually reach `Delivered` now — `AttachKey` alone only reaches `KeysAssigned`):

```csharp
    private static async Task<Guid> SeedOrderAsync(Infrastructure.Persistence.AppDbContext context, Guid userId, bool deliver = false)
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(userId, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);

        if (deliver)
        {
            var item = order.Items.Single();
            var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
            var (blob, version) = cipher.Encrypt("SEED-CODE");
            order.AttachKey(item.Id, new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now), now);
            order.MarkDelivered(now.AddSeconds(1));
        }

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter GetMyOrdersTests`
Expected: FAIL — `MyOrderItemDetail.Revealable` doesn't exist yet, `Keys` is still nullable.

- [ ] **Step 3: Implement**

In `src/Maxkeys.Application/Orders/OrdersDtos.cs`, replace `MyOrderItemDetail`:

```csharp
/// <summary>
/// One order item's detail (admin-key-delivery-gate spec: decision 2/4 — MODIFIED).
/// <see cref="Keys"/> only ever contains codes already revealed by the buyer via
/// <c>RevealOrderItemKeys</c> — never auto-populated just because the order is
/// <see cref="OrderStatus.Delivered"/>. <see cref="Revealable"/> tells the caller
/// whether the "revelar key" action is available for this item right now.
/// </summary>
public sealed record MyOrderItemDetail(string ProductName, string VariantName, decimal UnitPrice, int Quantity, IReadOnlyList<string> Keys, bool Revealable);
```

Replace `src/Maxkeys.Application/Orders/GetMyOrder.cs` in full:

```csharp
using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Orders;

/// <summary>
/// Resolves one order's detail scoped to its owner (orders-history spec: Order
/// Detail With Conditional Key Reveal, Ownership Enforcement; admin-key-delivery-gate
/// spec: decision 2/4 — MODIFIED). Returns <see langword="null"/> both for an
/// unknown order and for an order owned by someone else — the API layer maps
/// either case to 404, never revealing that the order exists (ADR-14; matches
/// <c>GetOrderStatus</c>/<c>GetProductBySlug</c>).
/// </summary>
public sealed class GetMyOrder
{
    private readonly IAppDbContext _db;
    private readonly KeyCipher _keyCipher;

    public GetMyOrder(IAppDbContext db, KeyCipher keyCipher)
    {
        _db = db;
        _keyCipher = keyCipher;
    }

    public async Task<MyOrderDetail?> ExecuteAsync(Guid orderId, Guid userId, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Keys)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null || order.UserId != userId)
        {
            return null;
        }

        var items = order.Items
            .Select(item => new MyOrderItemDetail(
                item.ProductNameSnapshot,
                item.VariantNameSnapshot,
                item.UnitPrice,
                item.Quantity,
                item.Keys
                    .Where(key => key.Status == KeyStatus.Revealed)
                    .Select(key => _keyCipher.Decrypt(key.EncryptedCode, key.KeyVersion))
                    .ToList(),
                order.Status == OrderStatus.Delivered && item.Keys.Any(key => key.Status == KeyStatus.Assigned)))
            .ToList();

        return new MyOrderDetail(order.Id, order.Status, order.TotalAmount, order.Currency, order.CreatedAt, items);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter GetMyOrdersTests`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Orders/GetMyOrder.cs src/Maxkeys.Application/Orders/OrdersDtos.cs tests/Maxkeys.Application.Tests/Orders/GetMyOrdersTests.cs
git commit -m "fix(vault): GetMyOrder only ever shows already-revealed key codes"
```

---

### Task 9: Application — `ListBuyers` exposes `RevealedKeys`

**Files:**
- Modify: `src/Maxkeys.Application/Buyers/BuyersDtos.cs`
- Modify: `src/Maxkeys.Application/Buyers/ListBuyers.cs:81-93` (`ToAdminBuyerOrder`)
- Test: `tests/Maxkeys.Application.Tests/Buyers/ListBuyersTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `tests/Maxkeys.Application.Tests/Buyers/ListBuyersTests.cs`, inside the class, after `Order_item_exposes_only_the_assigned_key_count`:

```csharp

    [Fact]
    public async Task Order_item_exposes_the_revealed_key_count_once_revealed()
    {
        var buyerEmail = $"buyer-{Guid.NewGuid():N}@example.com";
        var orderId = await SeedPaidOrderWithRevealedKeyAsync(buyerEmail);

        await using var context = _fixture.CreateContext();
        var result = await CreateSut(context).ExecuteAsync(email: buyerEmail, page: 1, pageSize: 20);

        var order = Assert.Single(result.Items).Orders.Single(o => o.Id == orderId);
        var item = Assert.Single(order.Items);
        Assert.Equal(0, item.AssignedKeys);
        Assert.Equal(1, item.RevealedKeys);
    }
```

And a new helper method at the end of the class, after `SeedPaidOrderWithAssignedKeyAsync`:

```csharp

    private async Task<Guid> SeedPaidOrderWithRevealedKeyAsync(string buyerEmail)
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null, buyerEmail, [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var item = order.Items.Single();
        var (blob, version) = cipher.Encrypt("CODE-ONE");
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
        context.Keys.Add(key);
        order.AttachKey(item.Id, key, now);
        key.Reveal(now);
        await context.SaveChangesAsync();

        return order.Id;
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter ListBuyersTests`
Expected: FAIL — `AdminBuyerOrderItem.RevealedKeys` doesn't exist yet.

- [ ] **Step 3: Implement**

In `src/Maxkeys.Application/Buyers/BuyersDtos.cs`, replace `AdminBuyerOrderItem`:

```csharp
/// <summary>One order item in the admin buyers view — exposes only key counts, never a key code (admin-buyers spec: Key Exposure in Buyer View; admin-key-delivery-gate spec: decision 3 adds RevealedKeys).</summary>
public sealed record AdminBuyerOrderItem(string ProductName, string VariantName, int Quantity, int AssignedKeys, int RevealedKeys);
```

In `src/Maxkeys.Application/Buyers/ListBuyers.cs`, replace the `ToAdminBuyerOrder` method:

```csharp
    private static AdminBuyerOrder ToAdminBuyerOrder(Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.PaidAt,
        order.TotalAmount,
        order.Currency,
        order.Items
            .Select(item => new AdminBuyerOrderItem(
                item.ProductNameSnapshot,
                item.VariantNameSnapshot,
                item.Quantity,
                item.Keys.Count(k => k.Status == KeyStatus.Assigned),
                item.Keys.Count(k => k.Status == KeyStatus.Revealed)))
            .ToList());
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Application.Tests --filter ListBuyersTests`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Application/Buyers/BuyersDtos.cs src/Maxkeys.Application/Buyers/ListBuyers.cs tests/Maxkeys.Application.Tests/Buyers/ListBuyersTests.cs
git commit -m "feat(vault): expose RevealedKeys count in the admin buyers listing"
```

---

### Task 10: Cleanup — remove `OrderDeliveredHandler`

**Files:**
- Delete: `src/Maxkeys.Application/Outbox/OrderDeliveredHandler.cs`
- Delete: `tests/Maxkeys.Application.Tests/Outbox/OrderDeliveredHandlerTests.cs`
- Modify: `src/Maxkeys.Application/Outbox/OutboxServiceCollectionExtensions.cs`
- Modify: `src/Maxkeys.Domain/Outbox/OutboxEventTypes.cs`
- Modify: `src/Maxkeys.Application/Notifications/EmailTemplates.cs:43`
- Modify: `src/Maxkeys.Application/Notifications/BuyerDeliveryItem.cs:5`
- Modify: `src/Maxkeys.Application/Outbox/IOutboxHandler.cs:9`
- Modify: `src/Maxkeys.Application/Outbox/DeliveryEmailItems.cs:10`

No new tests — this is pure removal of dead code (nothing enqueues `OutboxEventTypes.OrderDelivered` any more after Tasks 3–5). Verified by a full build+test pass at the end.

- [ ] **Step 1: Delete the dead handler and its test**

```bash
git rm src/Maxkeys.Application/Outbox/OrderDeliveredHandler.cs tests/Maxkeys.Application.Tests/Outbox/OrderDeliveredHandlerTests.cs
```

- [ ] **Step 2: Remove its DI registration and the unused event-type constant**

In `src/Maxkeys.Application/Outbox/OutboxServiceCollectionExtensions.cs`, remove the line:

```csharp
        services.AddScoped<IOutboxHandler, OrderDeliveredHandler>();
```

so the method body reads:

```csharp
    public static IServiceCollection AddOutboxHandlers(this IServiceCollection services)
    {
        services.AddScoped<IOutboxHandler, OrderApprovedHandler>();
        services.AddScoped<IOutboxHandler, OrderDeliveryResendHandler>();

        return services;
    }
```

In `src/Maxkeys.Domain/Outbox/OutboxEventTypes.cs`, remove the `OrderDelivered` constant and its doc line so the file reads:

```csharp
namespace Maxkeys.Domain.Outbox;

/// <summary>
/// Well-known <see cref="OutboxEvent.Type"/> values (design section 3;
/// outbox-processing spec: OrderApproved Handler).
/// </summary>
public static class OutboxEventTypes
{
    public const string OrderApproved = "OrderApproved";

    /// <summary>Admin-triggered resend of the delivery email for an already-<c>Delivered</c> order (admin-buyers spec: Resend Delivery Email; design D1).</summary>
    public const string OrderDeliveryResendRequested = "OrderDeliveryResendRequested";
}
```

- [ ] **Step 3: Fix the now-dangling `<see cref="OrderDeliveredHandler"/>` doc references**

In `src/Maxkeys.Application/Notifications/EmailTemplates.cs:43`, change:

```csharp
    /// Email; outbox-processing spec, <c>OrderDeliveredHandler</c>). <paramref name="items"/>
```

to:

```csharp
    /// Email — MODIFIED, admin-key-delivery-gate spec: now sent only via the admin-triggered
    /// <c>OrderDeliveryResendHandler</c>). <paramref name="items"/>
```

In `src/Maxkeys.Application/Notifications/BuyerDeliveryItem.cs:5`, change:

```csharp
/// Decryption happens in the caller (<c>OrderDeliveredHandler</c>) — templates never depend on <c>KeyCipher</c>.
```

to:

```csharp
/// Decryption happens in the caller (<c>OrderDeliveryResendHandler</c>) — templates never depend on <c>KeyCipher</c>.
```

In `src/Maxkeys.Application/Outbox/IOutboxHandler.cs:9`, change:

```csharp
/// <see cref="OrderApprovedHandler"/> and <see cref="OrderDeliveredHandler"/>).
```

to:

```csharp
/// <see cref="OrderApprovedHandler"/> and <see cref="OrderDeliveryResendHandler"/>).
```

In `src/Maxkeys.Application/Outbox/DeliveryEmailItems.cs:9-11`, change:

```csharp
/// Builds the per-item, decrypted-key content shared by the original delivery
/// email (<see cref="OrderDeliveredHandler"/>) and its admin-triggered resend
/// (<see cref="OrderDeliveryResendHandler"/>) (design D1). Decrypts each
```

to:

```csharp
/// Builds the per-item, decrypted-key content for the admin-triggered delivery
/// resend (<see cref="OrderDeliveryResendHandler"/>) (design D1; admin-key-delivery-gate
/// spec: decision 4 — the only remaining sender, delivery no longer auto-emails). Decrypts each
```

- [ ] **Step 4: Verify nothing else references the removed symbols, and the solution still builds and passes**

Run: `grep -rn "OrderDeliveredHandler\|OutboxEventTypes.OrderDelivered\b" src tests`
Expected: no matches.

Run: `dotnet build`
Expected: builds clean, no CS1574 (dangling doc-comment reference) or CS0246 (missing type) errors.

Run: `dotnet test tests/Maxkeys.Application.Tests`
Expected: PASS, full suite.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore(vault): remove OrderDeliveredHandler, nothing enqueues OrderDelivered any more"
```

---

### Task 11: API — admin `assign-keys` and `deliver` endpoints

**Files:**
- Modify: `src/Maxkeys.Api/Endpoints/AdminEndpoints.cs`
- Modify: `src/Maxkeys.Infrastructure/DependencyInjection.cs:58-85` (`AddUseCases`)
- Test: `tests/Maxkeys.Api.Tests/Admin/AssignKeysEndpointTests.cs` (new)
- Test: `tests/Maxkeys.Api.Tests/Admin/DeliverOrderEndpointTests.cs` (new)

- [ ] **Step 1: Write the failing tests**

Create `tests/Maxkeys.Api.Tests/Admin/AssignKeysEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Fulfillment;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>Covers <c>POST /admin/orders/{id}/assign-keys</c> (admin-key-delivery-gate spec: decision 3, "Asignar").</summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AssignKeysEndpointTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private readonly Hs256ApiTestFixture _factory;

    public AssignKeysEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().PostAsync($"/admin/orders/{Guid.NewGuid()}/assign-keys", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden()
    {
        var response = await AdminClient(NonAdminSub).PostAsync($"/admin/orders/{Guid.NewGuid()}/assign-keys", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_order_returns_404()
    {
        var response = await AdminClient().PostAsync($"/admin/orders/{Guid.NewGuid()}/assign-keys", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Order_with_full_vault_stock_is_fully_assigned()
    {
        var (orderId, variantId) = await SeedAwaitingFulfillmentVaultOrderAsync(quantity: 1);
        await LoadStockAsync(variantId, "CODE-1");

        var response = await AdminClient().PostAsync($"/admin/orders/{orderId}/assign-keys", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssignVaultKeysToOrderResult>();
        Assert.True(body!.AllItemsComplete);
        Assert.Equal(OrderStatus.KeysAssigned, body.OrderStatus);
    }

    private async Task<(Guid OrderId, Guid VariantId)> SeedAwaitingFulfillmentVaultOrderAsync(int quantity)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product($"p-{Guid.NewGuid():N}", "Vault Product", $"platform-{Guid.NewGuid():N}");
        product.SetVaultEnabled(true);
        db.Products.Add(product);
        var variant = new ProductVariant(product.Id, 1_000m, "ARS", region: "AR", edition: "Standard");
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(variant.Id, "Vault Product", "Standard", 1_000m, quantity)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return (order.Id, variant.Id);
    }

    private async Task LoadStockAsync(Guid variantId, params string[] codes)
    {
        using var scope = _factory.Services.CreateScope();
        var loadVaultKeys = scope.ServiceProvider.GetRequiredService<LoadVaultKeys>();
        await loadVaultKeys.ExecuteAsync(variantId, codes, "seed-admin");
    }

    private HttpClient AdminClient(string? sub = null)
    {
        var token = TestTokens.CreateHs256(
            sub ?? Hs256ApiTestFixture.AdminSub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
```

Create `tests/Maxkeys.Api.Tests/Admin/DeliverOrderEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>Covers <c>POST /admin/orders/{id}/deliver</c> (admin-key-delivery-gate spec: decision 3, "Entregar").</summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class DeliverOrderEndpointTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly Hs256ApiTestFixture _factory;

    public DeliverOrderEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().PostAsync($"/admin/orders/{Guid.NewGuid()}/deliver", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden()
    {
        var response = await AdminClient(NonAdminSub).PostAsync($"/admin/orders/{Guid.NewGuid()}/deliver", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_order_returns_404()
    {
        var response = await AdminClient().PostAsync($"/admin/orders/{Guid.NewGuid()}/deliver", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AwaitingFulfillment_order_returns_409()
    {
        var orderId = await SeedAwaitingFulfillmentOrderAsync();

        var response = await AdminClient().PostAsync($"/admin/orders/{orderId}/deliver", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task KeysAssigned_order_is_delivered()
    {
        var orderId = await SeedKeysAssignedOrderAsync();

        var response = await AdminClient().PostAsync($"/admin/orders/{orderId}/deliver", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeliverOrderResponse>();
        Assert.Equal(nameof(OrderStatus.Delivered), body!.Status);
    }

    private async Task<Guid> SeedAwaitingFulfillmentOrderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return order.Id;
    }

    private async Task<Guid> SeedKeysAssignedOrderAsync()
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(null, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var item = order.Items.Single();
        var (blob, version) = cipher.Encrypt("CODE-ONE");
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
        db.Keys.Add(key);
        order.AttachKey(item.Id, key, now);
        await db.SaveChangesAsync();

        return order.Id;
    }

    private HttpClient AdminClient(string? sub = null)
    {
        var token = TestTokens.CreateHs256(
            sub ?? Hs256ApiTestFixture.AdminSub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Api.Tests --filter "AssignKeysEndpointTests|DeliverOrderEndpointTests"`
Expected: FAIL — routes don't exist yet (404 on every request, including the ones expecting 401/403), `DeliverOrderResponse` doesn't exist.

- [ ] **Step 3: Implement**

In `src/Maxkeys.Infrastructure/DependencyInjection.cs`, in `AddUseCases`, add right after `services.AddScoped<AttachKeyToOrderItem>();`:

```csharp
        services.AddScoped<AssignVaultKeysToOrder>();
        services.AddScoped<DeliverOrder>();
```

In `src/Maxkeys.Api/Endpoints/AdminEndpoints.cs`, add two routes to `MapAdminEndpoints`, right after the existing `resend-delivery` route and before `return app;`:

```csharp
        group.MapPost("/{id:guid}/assign-keys", async (
            Guid id,
            AssignVaultKeysToOrder useCase,
            CancellationToken cancellationToken) =>
        {
            var result = await useCase.ExecuteAsync(id, cancellationToken);
            return result is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(result);
        });

        group.MapPost("/{id:guid}/deliver", async (
            Guid id,
            ClaimsPrincipal user,
            DeliverOrder useCase,
            CancellationToken cancellationToken) =>
        {
            var adminSub = user.FindFirst("sub")?.Value
                ?? throw new InvalidOperationException("Authenticated admin principal is missing a 'sub' claim.");

            var status = await useCase.ExecuteAsync(id, adminSub, cancellationToken);
            return status is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(new DeliverOrderResponse(status.Value.ToString()));
        });
```

Add the response record next to the other records at the bottom of the file:

```csharp
/// <summary>Response for <c>POST /admin/orders/{id}/deliver</c>.</summary>
public sealed record DeliverOrderResponse(string Status);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Api.Tests --filter "AssignKeysEndpointTests|DeliverOrderEndpointTests"`
Expected: PASS (4 + 5 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Api/Endpoints/AdminEndpoints.cs src/Maxkeys.Infrastructure/DependencyInjection.cs tests/Maxkeys.Api.Tests/Admin/AssignKeysEndpointTests.cs tests/Maxkeys.Api.Tests/Admin/DeliverOrderEndpointTests.cs
git commit -m "feat(vault): add admin assign-keys and deliver endpoints"
```

---

### Task 12: API — buyer reveal endpoint

**Files:**
- Modify: `src/Maxkeys.Api/Endpoints/MeEndpoints.cs`
- Modify: `src/Maxkeys.Infrastructure/DependencyInjection.cs:58-85` (`AddUseCases`)
- Test: `tests/Maxkeys.Api.Tests/Orders/RevealKeysEndpointTests.cs` (new)

- [ ] **Step 1: Write the failing tests**

Create `tests/Maxkeys.Api.Tests/Orders/RevealKeysEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Orders;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Tests.Orders;

/// <summary>Covers <c>POST /me/orders/{id}/items/{itemId}/keys/reveal</c> (admin-key-delivery-gate spec: decision 4, buyer reveal).</summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class RevealKeysEndpointTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly Hs256ApiTestFixture _factory;

    public RevealKeysEndpointTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().PostAsync(
            $"/me/orders/{Guid.NewGuid()}/items/{Guid.NewGuid()}/keys/reveal", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_order_returns_404()
    {
        var userId = Guid.NewGuid();
        var response = await BuyerClient(userId).PostAsync(
            $"/me/orders/{Guid.NewGuid()}/items/{Guid.NewGuid()}/keys/reveal", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_strangers_order_returns_404()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(owner);

        var response = await BuyerClient(stranger).PostAsync(
            $"/me/orders/{orderId}/items/{itemId}/keys/reveal", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Order_not_yet_delivered_returns_409()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedKeysAssignedOrderAsync(userId);

        var response = await BuyerClient(userId).PostAsync(
            $"/me/orders/{orderId}/items/{itemId}/keys/reveal", content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delivered_order_reveals_the_key_code()
    {
        var userId = Guid.NewGuid();
        var (orderId, itemId) = await SeedDeliveredOrderAsync(userId);

        var response = await BuyerClient(userId).PostAsync(
            $"/me/orders/{orderId}/items/{itemId}/keys/reveal", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RevealOrderItemKeysResponse>();
        Assert.Equal(["SEED-CODE"], body!.Codes);
    }

    private async Task<(Guid OrderId, Guid ItemId)> SeedKeysAssignedOrderAsync(Guid userId)
    {
        var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
        var now = DateTimeOffset.UtcNow;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = Order.Create(userId, $"buyer-{Guid.NewGuid():N}@example.com", [new OrderLine(Guid.NewGuid(), "Product", "Standard", 1_000m, 1)], now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var item = order.Items.Single();
        var (blob, version) = cipher.Encrypt("SEED-CODE");
        var key = new Maxkeys.Domain.Keys.Key(item.ProductVariantId, blob, version, "seed-admin", now);
        db.Keys.Add(key);
        order.AttachKey(item.Id, key, now);
        await db.SaveChangesAsync();

        return (order.Id, item.Id);
    }

    private async Task<(Guid OrderId, Guid ItemId)> SeedDeliveredOrderAsync(Guid userId)
    {
        var (orderId, itemId) = await SeedKeysAssignedOrderAsync(userId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        order.MarkDelivered(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        return (orderId, itemId);
    }

    private HttpClient BuyerClient(Guid userId)
    {
        var token = TestTokens.CreateHs256(
            userId.ToString(), Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Maxkeys.Api.Tests --filter RevealKeysEndpointTests`
Expected: FAIL — route doesn't exist yet, `RevealOrderItemKeysResponse` doesn't exist.

- [ ] **Step 3: Implement**

In `src/Maxkeys.Infrastructure/DependencyInjection.cs`, in `AddUseCases`, add right after `services.AddScoped<ListBuyers>();`:

```csharp
        services.AddScoped<RevealOrderItemKeys>();
```

In `src/Maxkeys.Api/Endpoints/MeEndpoints.cs`, add a route inside `MapMeEndpoints`, right after the existing `/{id:guid}` route and before `return app;`:

```csharp
        group.MapPost("/{id:guid}/items/{itemId:guid}/keys/reveal", async (
            Guid id,
            Guid itemId,
            ClaimsPrincipal user,
            RevealOrderItemKeys useCase,
            CancellationToken cancellationToken) =>
        {
            var codes = await useCase.ExecuteAsync(id, itemId, RequireUserId(user), cancellationToken);
            return codes is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
                : Results.Ok(new RevealOrderItemKeysResponse(codes));
        });
```

Add the response record after the class closing brace:

```csharp

/// <summary>Response for <c>POST /me/orders/{id}/items/{itemId}/keys/reveal</c>.</summary>
public sealed record RevealOrderItemKeysResponse(IReadOnlyList<string> Codes);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Maxkeys.Api.Tests --filter RevealKeysEndpointTests`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Maxkeys.Api/Endpoints/MeEndpoints.cs src/Maxkeys.Infrastructure/DependencyInjection.cs tests/Maxkeys.Api.Tests/Orders/RevealKeysEndpointTests.cs
git commit -m "feat(vault): add buyer reveal-key endpoint"
```

---

### Task 13: Migration and full solution verification

**Files:**
- Create (generated): `src/Maxkeys.Infrastructure/Persistence/Migrations/*_AddKeyRevealedAt.cs` (+ `.Designer.cs`, updated `AppDbContextModelSnapshot.cs`)

No new mapping needed in `KeyConfiguration.cs` — `RevealedAt` is convention-mapped exactly like the existing `AssignedAt`, no explicit configuration.

- [ ] **Step 1: Generate the migration**

Run:
```bash
dotnet ef migrations add AddKeyRevealedAt --project src/Maxkeys.Infrastructure --startup-project src/Maxkeys.Api --output-dir Persistence/Migrations
```

- [ ] **Step 2: Inspect the generated migration**

Open the new `*_AddKeyRevealedAt.cs` and confirm `Up` contains exactly one `AddColumn<DateTimeOffset>` (nullable) for `revealed_at` on table `keys`, and `Down` drops it — nothing else. If EF generated anything beyond that column (e.g. picked up an unrelated pending model change), stop and investigate before proceeding — do not blindly accept an unexpected diff.

- [ ] **Step 3: Build and run the full test suite**

Run: `dotnet build`
Expected: builds clean.

Run: `dotnet test`
Expected: PASS, every test project (`Maxkeys.Domain.Tests`, `Maxkeys.Application.Tests`, `Maxkeys.Api.Tests`).

- [ ] **Step 4: Commit**

```bash
git add src/Maxkeys.Infrastructure/Persistence/Migrations
git commit -m "feat(vault): add EF Core migration for Key.RevealedAt"
```

---

## Self-Review Notes

- **Spec coverage:** decision 1 (KeysAssigned gate) → Tasks 1, 4, 5; decision 2 (per-item reveal) → Tasks 2, 7, 8; decision 3 (two admin buttons) → Tasks 3, 6, 9, 11; decision 4 (no auto email) → Tasks 4, 5, 10. Migration → Task 13.
- **Type consistency checked:** `AssignVaultKeysToOrderResult`/`Item` (Task 3) used identically in `AdminEndpoints.cs` (Task 11) and its test. `DeliverOrder.ExecuteAsync` returns `OrderStatus?`, wrapped as `DeliverOrderResponse(string Status)` in the API layer (Task 11) — matches `AttachKeyResponse`'s existing `.ToString()` convention. `RevealOrderItemKeys.ExecuteAsync` returns `IReadOnlyList<string>?`, wrapped as `RevealOrderItemKeysResponse(IReadOnlyList<string> Codes)` (Task 12). `MyOrderItemDetail.Keys` changed from nullable to non-nullable consistently across Task 8's DTO and use-case edit.
- **No placeholders:** every step above has literal, runnable code — no TBD/TODO.
