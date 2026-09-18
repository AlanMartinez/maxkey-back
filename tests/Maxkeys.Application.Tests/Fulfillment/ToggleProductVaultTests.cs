using Maxkeys.Application.Fulfillment;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Tests.Fulfillment;

/// <summary>Covers <see cref="ToggleProductVault"/> (vault spec: Per-Product Vault Toggle).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ToggleProductVaultTests
{
    private readonly PostgresFixture _fixture;

    public ToggleProductVaultTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Enabling_the_vault_persists_the_flag()
    {
        var productId = await SeedProductAsync();

        await using var context = _fixture.CreateContext();
        var sut = new ToggleProductVault(context);

        var result = await sut.ExecuteAsync(productId, true);

        Assert.True(result);

        await using var readContext = _fixture.CreateContext();
        var product = await readContext.Products.SingleAsync(p => p.Id == productId);
        Assert.True(product.VaultEnabled);
    }

    [Fact]
    public async Task Disabling_an_enabled_vault_persists_the_flag()
    {
        var productId = await SeedProductAsync();

        await using (var firstContext = _fixture.CreateContext())
        {
            await new ToggleProductVault(firstContext).ExecuteAsync(productId, true);
        }

        await using var context = _fixture.CreateContext();
        var result = await new ToggleProductVault(context).ExecuteAsync(productId, false);

        Assert.False(result);
    }

    [Fact]
    public async Task Unknown_product_returns_null()
    {
        await using var context = _fixture.CreateContext();
        var sut = new ToggleProductVault(context);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), true);

        Assert.Null(result);
    }

    private async Task<Guid> SeedProductAsync()
    {
        await using var context = _fixture.CreateContext();
        var product = new Product($"p-{Guid.NewGuid():N}", "Product", $"platform-{Guid.NewGuid():N}");
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product.Id;
    }
}
