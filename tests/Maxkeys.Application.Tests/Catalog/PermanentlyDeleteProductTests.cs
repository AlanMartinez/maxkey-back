using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Carousel;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Keys;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Wishlist;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Tests.Catalog;

[Collection(PostgresCollection.Name)]
public sealed class PermanentlyDeleteProductTests
{
    private readonly PostgresFixture _fixture;

    public PermanentlyDeleteProductTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Returns_not_found_for_unknown_id()
    {
        await using var context = _fixture.CreateContext();
        var sut = new PermanentlyDeleteProduct(context);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), force: false);

        Assert.Equal(PermanentlyDeleteProductOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Removes_the_product_and_all_its_dependents_when_it_has_no_keys()
    {
        var seeded = await SeedProductGraphAsync();

        await using var context = _fixture.CreateContext();
        var sut = new PermanentlyDeleteProduct(context);

        var result = await sut.ExecuteAsync(seeded.ProductId, force: false);

        Assert.Equal(PermanentlyDeleteProductOutcome.Deleted, result.Outcome);
        await using var verify = _fixture.CreateContext();
        Assert.Null(await verify.Products.FindAsync(seeded.ProductId));
        Assert.False(await verify.ProductVariants.AnyAsync(v => v.ProductId == seeded.ProductId));
        Assert.False(await verify.ProductImages.AnyAsync(i => i.ProductId == seeded.ProductId));
        Assert.False(await verify.WishlistItems.AnyAsync(w => w.ProductId == seeded.ProductId));
        Assert.False(await verify.CarouselSlides.AnyAsync(s => s.ProductId == seeded.ProductId));
    }

    [Fact]
    public async Task Returns_conflict_with_the_available_key_count_and_changes_nothing_without_force()
    {
        var seeded = await SeedProductGraphAsync();
        var keyIds = await SeedAvailableKeysAsync(seeded.VariantId, count: 2);

        await using var context = _fixture.CreateContext();
        var sut = new PermanentlyDeleteProduct(context);

        var result = await sut.ExecuteAsync(seeded.ProductId, force: false);

        Assert.Equal(PermanentlyDeleteProductOutcome.Conflict, result.Outcome);
        Assert.Equal(2, result.AvailableKeyCount);
        await using var verify = _fixture.CreateContext();
        Assert.NotNull(await verify.Products.FindAsync(seeded.ProductId));
        Assert.NotNull(await verify.ProductVariants.FindAsync(seeded.VariantId));
        Assert.True(await verify.ProductImages.AnyAsync(i => i.ProductId == seeded.ProductId));
        Assert.True(await verify.WishlistItems.AnyAsync(w => w.ProductId == seeded.ProductId));
        Assert.True(await verify.CarouselSlides.AnyAsync(s => s.ProductId == seeded.ProductId));
        var keys = await verify.Keys.Where(k => keyIds.Contains(k.Id)).ToListAsync();
        Assert.All(keys, k => Assert.Equal(seeded.VariantId, k.ProductVariantId));
    }

    [Fact]
    public async Task Force_deletes_the_product_orphans_available_keys_and_keeps_sold_keys_untouched()
    {
        var seeded = await SeedProductGraphAsync();
        var availableKeyIds = await SeedAvailableKeysAsync(seeded.VariantId, count: 2);
        var (assignedKeyId, revealedKeyId) = await SeedSoldKeysAsync(seeded.VariantId);

        await using var context = _fixture.CreateContext();
        var sut = new PermanentlyDeleteProduct(context);

        var result = await sut.ExecuteAsync(seeded.ProductId, force: true);

        Assert.Equal(PermanentlyDeleteProductOutcome.Deleted, result.Outcome);
        await using var verify = _fixture.CreateContext();
        Assert.Null(await verify.Products.FindAsync(seeded.ProductId));
        Assert.Null(await verify.ProductVariants.FindAsync(seeded.VariantId));
        Assert.False(await verify.ProductImages.AnyAsync(i => i.ProductId == seeded.ProductId));
        Assert.False(await verify.WishlistItems.AnyAsync(w => w.ProductId == seeded.ProductId));
        Assert.False(await verify.CarouselSlides.AnyAsync(s => s.ProductId == seeded.ProductId));

        var availableKeys = await verify.Keys.Where(k => availableKeyIds.Contains(k.Id)).ToListAsync();
        Assert.Equal(2, availableKeys.Count);
        Assert.All(availableKeys, k =>
        {
            Assert.Null(k.ProductVariantId);
            Assert.Equal(KeyStatus.Available, k.Status);
        });

        var assignedKey = await verify.Keys.SingleAsync(k => k.Id == assignedKeyId);
        Assert.Equal(KeyStatus.Assigned, assignedKey.Status);
        Assert.Equal(seeded.VariantId, assignedKey.ProductVariantId);

        var revealedKey = await verify.Keys.SingleAsync(k => k.Id == revealedKeyId);
        Assert.Equal(KeyStatus.Revealed, revealedKey.Status);
        Assert.Equal(seeded.VariantId, revealedKey.ProductVariantId);
    }

    private async Task<(Guid ProductId, Guid VariantId)> SeedProductGraphAsync()
    {
        await using var seed = _fixture.CreateContext();
        var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
        await seed.SaveChangesAsync();
        CatalogTestData.SeedVariant(seed, product.Id, price: 100m);
        seed.ProductImages.Add(new ProductImage(product.Id, "products/gallery/1.png", 0));
        seed.WishlistItems.Add(new WishlistItem(Guid.NewGuid(), product.Id, DateTimeOffset.UtcNow));
        seed.CarouselSlides.Add(new CarouselSlide(product.Id, sortOrder: 0));
        await seed.SaveChangesAsync();
        var variantId = seed.ProductVariants.Single(v => v.ProductId == product.Id).Id;
        return (product.Id, variantId);
    }

    private async Task<List<Guid>> SeedAvailableKeysAsync(Guid variantId, int count)
    {
        await using var seed = _fixture.CreateContext();
        var keys = Enumerable.Range(0, count)
            .Select(_ => new Key(variantId, [1, 2, 3], 1, "seed-admin", DateTimeOffset.UtcNow))
            .ToList();
        seed.Keys.AddRange(keys);
        await seed.SaveChangesAsync();
        return keys.Select(k => k.Id).ToList();
    }

    private async Task<(Guid AssignedKeyId, Guid RevealedKeyId)> SeedSoldKeysAsync(Guid variantId)
    {
        var now = DateTimeOffset.UtcNow;
        await using var seed = _fixture.CreateContext();
        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(variantId, "Product", "Standard", 1_000m, 2)],
            now);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", now);
        order.MarkAwaitingFulfillment(now);
        seed.Orders.Add(order);
        await seed.SaveChangesAsync();

        var item = order.Items.Single();
        var assigned = new Key(variantId, [1, 2, 3], 1, "seed-admin", now);
        var revealed = new Key(variantId, [4, 5, 6], 1, "seed-admin", now);
        seed.Keys.AddRange(assigned, revealed);
        order.AttachKey(item.Id, assigned, now);
        order.AttachKey(item.Id, revealed, now);
        revealed.Reveal(now, "buyer-sub");
        await seed.SaveChangesAsync();

        return (assigned.Id, revealed.Id);
    }
}
