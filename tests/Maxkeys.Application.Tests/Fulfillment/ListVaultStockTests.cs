using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Security;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>Covers <see cref="ListVaultStock"/> (vault spec: Vault Stock Listing).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ListVaultStockTests
{
    private static readonly string ValidKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgresFixture _fixture;

    public ListVaultStockTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Lists_product_vault_flag_and_per_variant_key_counts()
    {
        Guid productId, variantId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Vault Product", $"platform-{Guid.NewGuid():N}");
            product.SetVaultEnabled(true);
            seedContext.Products.Add(product);
            var variant = new ProductVariant(product.Id, 100m, "ARS", region: "AR", edition: "Standard");
            seedContext.ProductVariants.Add(variant);
            await seedContext.SaveChangesAsync();
            productId = product.Id;
            variantId = variant.Id;
        }

        await using (var loadContext = _fixture.CreateContext())
        {
            var cipher = new KeyCipher(Options.Create(new KeyCipherOptions { EncryptionKey = ValidKeyBase64, CurrentVersion = 1 }));
            await new LoadVaultKeys(loadContext, cipher).ExecuteAsync(variantId, ["CODE-1", "CODE-2"], "admin@maxkeys.test");
        }

        await using var context = _fixture.CreateContext();
        var sut = new ListVaultStock(context);

        var products = await sut.ExecuteAsync();

        var vaultProduct = Assert.Single(products, p => p.Id == productId);
        Assert.True(vaultProduct.VaultEnabled);
        var variantStock = Assert.Single(vaultProduct.Variants, v => v.Id == variantId);
        Assert.Equal(2, variantStock.AvailableCount);
        Assert.Equal(0, variantStock.AssignedCount);
    }

    [Fact]
    public async Task Product_with_no_variants_has_an_empty_variant_list()
    {
        Guid productId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Empty Product", $"platform-{Guid.NewGuid():N}");
            seedContext.Products.Add(product);
            await seedContext.SaveChangesAsync();
            productId = product.Id;
        }

        await using var context = _fixture.CreateContext();
        var sut = new ListVaultStock(context);

        var products = await sut.ExecuteAsync();

        var listed = Assert.Single(products, p => p.Id == productId);
        Assert.False(listed.VaultEnabled);
        Assert.Empty(listed.Variants);
    }
}
