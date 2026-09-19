using Maxkeys.Application.Security;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Infrastructure.Persistence;

/// <summary>
/// Seeds a fixed set of mock vault stock and buyers that exercises every
/// admin vault/buyers scenario (stock levels, vault toggle, order statuses,
/// partial fulfillment, pagination). Development only — wired via
/// <c>Maxkeys.Api --seed-dev</c> (<c>Program.cs</c>), which refuses to run
/// outside the Development environment. Everything goes through the domain
/// methods (<see cref="Order.Create"/> → <see cref="Order.MarkPaid"/> →
/// <see cref="Order.MarkAwaitingFulfillment"/> → <see cref="Order.AttachKey"/>)
/// and the real <see cref="KeyCipher"/>, so the rows are indistinguishable from
/// production-shaped data. Idempotent: if the marker product already exists the
/// seeder logs and exits; drop the database and re-run <c>--migrate</c> to reseed.
/// </summary>
public static class DevDataSeeder
{
    private const string MarkerSlug = "dev-vault-full-stock";
    private const string LoadedBy = "dev-seeder";

    public static async Task<bool> SeedAsync(AppDbContext db, KeyCipher keyCipher, CancellationToken cancellationToken = default)
    {
        if (await db.Products.AnyAsync(p => p.Slug == MarkerSlug, cancellationToken))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var keyCounter = 0;

        // --- Vault scenarios -------------------------------------------------

        // 1. Vault ON, plenty of stock: available + assigned mix.
        var fullStock = AddProduct(db, MarkerSlug, "Dev Vault Full Stock", "Steam", vaultEnabled: true);
        var fullStockGlobal = AddVariant(db, fullStock, 15000m, "Global", "Standard", 0);
        var fullStockKeys = AddKeys(db, keyCipher, fullStockGlobal, 20, now, ref keyCounter);

        // 2. Vault ON, stock exhausted: variant exists, zero keys.
        var outOfStock = AddProduct(db, "dev-vault-out-of-stock", "Dev Vault Out Of Stock", "Xbox", vaultEnabled: true);
        var outOfStockVariant = AddVariant(db, outOfStock, 22000m, "LATAM", "Standard", 0);

        // 3. Vault ON, two variants: one stocked, one empty.
        var mixed = AddProduct(db, "dev-vault-mixed-variants", "Dev Vault Mixed Variants", "PlayStation", vaultEnabled: true);
        var mixedStocked = AddVariant(db, mixed, 30000m, "Global", "Standard", 0);
        var mixedEmpty = AddVariant(db, mixed, 45000m, "Global", "Deluxe", 1);
        var mixedStockedKeys = AddKeys(db, keyCipher, mixedStocked, 3, now, ref keyCounter);

        // 4. Vault OFF but keys loaded: stock visible, never auto-assigned.
        var vaultOff = AddProduct(db, "dev-vault-disabled-with-keys", "Dev Vault Disabled With Keys", "Nintendo", vaultEnabled: false);
        var vaultOffVariant = AddVariant(db, vaultOff, 18000m, "US", "Standard", 0);
        var vaultOffKeys = AddKeys(db, keyCipher, vaultOffVariant, 5, now, ref keyCounter);

        // 5. Never loaded any keys, vault OFF.
        var noKeys = AddProduct(db, "dev-vault-never-loaded", "Dev Vault Never Loaded", "Epic", vaultEnabled: false);
        var noKeysVariant = AddVariant(db, noKeys, 9000m, null, null, 0);

        // 6. Inactive product with keys.
        var inactive = AddProduct(db, "dev-vault-inactive", "Dev Vault Inactive Product", "GOG", vaultEnabled: true, isActive: false);
        var inactiveVariant = AddVariant(db, inactive, 12000m, "Global", "Standard", 0);
        AddKeys(db, keyCipher, inactiveVariant, 2, now, ref keyCounter);

        await db.SaveChangesAsync(cancellationToken);

        // --- Buyer scenarios -------------------------------------------------

        // 1. Guest buyer, single Delivered order (all keys assigned).
        var guestOrder = CreatePaidOrder(db, null, "guest.delivered@dev.local",
            [Line(fullStock, fullStockGlobal, 2)], now.AddDays(-10));
        Deliver(guestOrder, fullStockKeys.Take(2), now.AddDays(-10));

        // 2. Registered buyer with three orders in different states.
        var registeredUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        const string registeredEmail = "registered.buyer@dev.local";

        var registeredDelivered = CreatePaidOrder(db, registeredUserId, registeredEmail,
            [Line(fullStock, fullStockGlobal, 1)], now.AddDays(-7));
        Deliver(registeredDelivered, fullStockKeys.Skip(2).Take(1), now.AddDays(-7));

        var registeredPartial = CreatePaidOrder(db, registeredUserId, registeredEmail,
            [Line(mixed, mixedStocked, 2)], now.AddDays(-3));
        registeredPartial.MarkAwaitingFulfillment(now.AddDays(-3));
        // Only 1 of 2 keys assigned: stays AwaitingFulfillment.
        registeredPartial.AttachKey(registeredPartial.Items[0].Id, mixedStockedKeys[0], now.AddDays(-3));

        CreatePaidOrder(db, registeredUserId, registeredEmail,
            [Line(outOfStock, outOfStockVariant, 1)], now.AddDays(-1));
        // Left as Paid: outbox never processed it.

        // 3. Multi-item order mixing vault and non-vault products: vault item complete, other pending.
        var multiItem = CreatePaidOrder(db, null, "multi.item@dev.local",
            [Line(fullStock, fullStockGlobal, 1), Line(vaultOff, vaultOffVariant, 1)], now.AddDays(-2));
        multiItem.MarkAwaitingFulfillment(now.AddDays(-2));
        multiItem.AttachKey(multiItem.Items[0].Id, fullStockKeys[3], now.AddDays(-2));

        // 4. Three different products in one order, every item fulfilled → Delivered.
        var multiProduct = CreatePaidOrder(db, null, "multi.product.delivered@dev.local",
            [Line(fullStock, fullStockGlobal, 1), Line(vaultOff, vaultOffVariant, 1), Line(mixed, mixedStocked, 1)],
            now.AddDays(-4));
        Deliver(multiProduct, [fullStockKeys[5], vaultOffKeys[0], mixedStockedKeys[1]], now.AddDays(-4));

        // 5. Same product, two different variants: Standard has stock, Deluxe does not → AwaitingFulfillment.
        var sameProductVariants = CreatePaidOrder(db, null, "same.product.variants@dev.local",
            [Line(mixed, mixedStocked, 1), Line(mixed, mixedEmpty, 1)], now.AddDays(-2));
        sameProductVariants.MarkAwaitingFulfillment(now.AddDays(-2));
        sameProductVariants.AttachKey(sameProductVariants.Items[0].Id, mixedStockedKeys[2], now.AddDays(-2));

        // 6. Same variant, quantity 3 in a single item → Delivered with three keys on one item.
        var sameVariantQty = CreatePaidOrder(db, null, "same.variant.qty@dev.local",
            [Line(fullStock, fullStockGlobal, 3)], now.AddDays(-6));
        Deliver(sameVariantQty, fullStockKeys.Skip(6).Take(3), now.AddDays(-6));

        // 7. Registered buyer with a large mixed cart: two products, two quantities, all fulfilled → Delivered.
        var bigCartUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var bigCart = CreatePaidOrder(db, bigCartUserId, "big.cart@dev.local",
            [Line(fullStock, fullStockGlobal, 2), Line(vaultOff, vaultOffVariant, 2)], now.AddDays(-8));
        Deliver(bigCart, [fullStockKeys[9], fullStockKeys[10], vaultOffKeys[1], vaultOffKeys[2]], now.AddDays(-8));

        // 8. Negative cases: Pending and Cancelled orders must not appear in the buyers list.
        var pending = Order.Create(null, "never.paid@dev.local", [Line(fullStock, fullStockGlobal, 1)], now.AddHours(-2));
        db.Orders.Add(pending);

        var cancelled = Order.Create(null, "never.paid@dev.local", [Line(mixed, mixedEmpty, 1)], now.AddHours(-5));
        cancelled.Cancel(now.AddHours(-4));
        db.Orders.Add(cancelled);

        // 9. Mixed-case email to exercise case-insensitive search.
        var mixedCase = CreatePaidOrder(db, null, "MixedCase.Buyer@Dev.Local",
            [Line(fullStock, fullStockGlobal, 1)], now.AddDays(-5));
        Deliver(mixedCase, fullStockKeys.Skip(4).Take(1), now.AddDays(-5));

        // 10. Bulk buyers to force pagination (> default page size of 20).
        for (var i = 1; i <= 25; i++)
        {
            CreatePaidOrder(db, null, $"bulk.buyer{i:00}@dev.local",
                [Line(noKeys, noKeysVariant, 1)], now.AddDays(-30).AddHours(i));
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static Product AddProduct(AppDbContext db, string slug, string name, string platform, bool vaultEnabled, bool isActive = true)
    {
        var product = new Product(slug, name, platform, isActive, description: $"Dev seed product: {name}.");
        product.SetVaultEnabled(vaultEnabled);
        db.Products.Add(product);
        return product;
    }

    private static ProductVariant AddVariant(AppDbContext db, Product product, decimal price, string? region, string? edition, int sortOrder)
    {
        var variant = new ProductVariant(product.Id, price, "ARS", null, region, edition, sortOrder);
        db.ProductVariants.Add(variant);
        return variant;
    }

    private static List<Key> AddKeys(AppDbContext db, KeyCipher keyCipher, ProductVariant variant, int count, DateTimeOffset now, ref int counter)
    {
        var keys = new List<Key>(count);
        for (var i = 0; i < count; i++)
        {
            counter++;
            var (blob, version) = keyCipher.Encrypt($"DEV-{variant.Region ?? "XX"}-{counter:0000}");
            var key = new Key(variant.Id, blob, version, LoadedBy, now.AddMinutes(counter));
            db.Keys.Add(key);
            keys.Add(key);
        }

        return keys;
    }

    private static OrderLine Line(Product product, ProductVariant variant, int quantity) =>
        new(variant.Id, product.Name, $"{variant.Region ?? "Global"} / {variant.Edition ?? "Standard"}", variant.Price, quantity);

    private static Order CreatePaidOrder(AppDbContext db, Guid? userId, string email, IReadOnlyCollection<OrderLine> lines, DateTimeOffset paidAt)
    {
        var order = Order.Create(userId, email, lines, paidAt.AddMinutes(-5));
        order.AttachPreference($"dev-pref-{order.Id:N}");
        order.MarkPaid($"dev-pay-{order.Id:N}", paidAt);
        db.Orders.Add(order);
        return order;
    }

    private static void Deliver(Order order, IEnumerable<Key> keys, DateTimeOffset at)
    {
        order.MarkAwaitingFulfillment(at);
        foreach (var item in order.Items)
        {
            foreach (var key in keys.Where(k => k.ProductVariantId == item.ProductVariantId).Take(item.Quantity))
            {
                order.AttachKey(item.Id, key, at);
            }
        }
    }
}
