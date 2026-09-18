using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>Covers <see cref="LoadVaultKeys"/> (vault spec: Bulk Key Load).</summary>
[Collection(PostgresCollection.Name)]
public sealed class LoadVaultKeysTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public LoadVaultKeysTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Loading_codes_creates_available_keys_for_the_variant()
    {
        var variantId = await SeedVariantAsync();

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var result = await sut.ExecuteAsync(variantId, ["CODE-1", "CODE-2", "CODE-3"], "admin@maxkeys.test");

        Assert.NotNull(result);
        Assert.Equal(3, result!.AddedCount);
        Assert.Equal(3, result.AvailableCount);

        var keys = await context.Keys.Where(k => k.ProductVariantId == variantId).ToListAsync();
        Assert.Equal(3, keys.Count);
        Assert.All(keys, k => Assert.Equal(KeyStatus.Available, k.Status));
        Assert.All(keys, k => Assert.Equal("admin@maxkeys.test", k.LoadedBy));
    }

    [Fact]
    public async Task Loading_more_codes_adds_to_existing_available_count()
    {
        var variantId = await SeedVariantAsync();

        await using (var firstContext = _fixture.CreateContext())
        {
            await CreateSut(firstContext).ExecuteAsync(variantId, ["CODE-1"], "admin@maxkeys.test");
        }

        await using var context = _fixture.CreateContext();
        var result = await CreateSut(context).ExecuteAsync(variantId, ["CODE-2", "CODE-3"], "admin@maxkeys.test");

        Assert.NotNull(result);
        Assert.Equal(2, result!.AddedCount);
        Assert.Equal(3, result.AvailableCount);
    }

    [Fact]
    public async Task Empty_code_list_throws()
    {
        var variantId = await SeedVariantAsync();

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        await Assert.ThrowsAsync<DomainException>(() => sut.ExecuteAsync(variantId, [], "admin@maxkeys.test"));
    }

    [Fact]
    public async Task Blank_code_in_the_list_throws()
    {
        var variantId = await SeedVariantAsync();

        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        await Assert.ThrowsAsync<DomainException>(() => sut.ExecuteAsync(variantId, ["CODE-1", "   "], "admin@maxkeys.test"));
    }

    [Fact]
    public async Task Unknown_variant_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var sut = CreateSut(context);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), ["CODE-1"], "admin@maxkeys.test");

        Assert.Null(result);
    }

    private static LoadVaultKeys CreateSut(AppDbContext context) =>
        new(context, new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 })));

    private async Task<Guid> SeedVariantAsync()
    {
        await using var context = _fixture.CreateContext();
        var product = new Product($"p-{Guid.NewGuid():N}", "Product", $"platform-{Guid.NewGuid():N}");
        context.Products.Add(product);
        var variant = new ProductVariant(product.Id, 100m, "ARS", region: "AR", edition: "Standard");
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();
        return variant.Id;
    }
}
