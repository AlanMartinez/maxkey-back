using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>Covers <see cref="ListVariantKeys"/> (vault spec: Vault Key Audit).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ListVariantKeysTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public ListVariantKeysTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Lists_loaded_keys_newest_first_without_encrypted_code()
    {
        var variantId = await SeedVariantAsync();

        await using (var loadContext = _fixture.CreateContext())
        {
            var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
            await new LoadVaultKeys(loadContext, cipher).ExecuteAsync(variantId, ["CODE-1"], "admin@maxkeys.test");
            await new LoadVaultKeys(loadContext, cipher).ExecuteAsync(variantId, ["CODE-2"], "admin@maxkeys.test");
        }

        await using var context = _fixture.CreateContext();
        var sut = new ListVariantKeys(context);

        var keys = await sut.ExecuteAsync(variantId);

        Assert.NotNull(keys);
        Assert.Equal(2, keys!.Count);
        Assert.True(keys[0].CreatedAt >= keys[1].CreatedAt);
        Assert.All(keys, k => Assert.Equal("Available", k.Status));
        Assert.All(keys, k => Assert.Equal("admin@maxkeys.test", k.LoadedBy));
        Assert.All(keys, k => Assert.Null(k.AssignedAt));
        Assert.All(keys, k => Assert.Null(k.OrderItemId));
        Assert.All(keys, k => Assert.NotEqual(default, k.CreatedAt));
    }

    [Fact]
    public async Task Variant_with_no_keys_returns_an_empty_list()
    {
        var variantId = await SeedVariantAsync();

        await using var context = _fixture.CreateContext();
        var sut = new ListVariantKeys(context);

        var keys = await sut.ExecuteAsync(variantId);

        Assert.NotNull(keys);
        Assert.Empty(keys!);
    }

    [Fact]
    public async Task Unknown_variant_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var sut = new ListVariantKeys(context);

        var keys = await sut.ExecuteAsync(Guid.NewGuid());

        Assert.Null(keys);
    }

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
