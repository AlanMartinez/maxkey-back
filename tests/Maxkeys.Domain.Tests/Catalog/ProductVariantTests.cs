using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Tests.Catalog;

public class ProductVariantTests
{
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public void Constructor_WithValidData_CreatesActiveVariant()
    {
        var variant = new ProductVariant(ProductId, price: 1000m, currency: "ARS");

        Assert.Equal(1000m, variant.Price);
        Assert.True(variant.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositivePrice_Throws(decimal price)
    {
        Assert.Throws<DomainException>(() => new ProductVariant(ProductId, price, "ARS"));
    }

    [Fact]
    public void Constructor_WithOldPriceNotGreaterThanPrice_Throws()
    {
        Assert.Throws<DomainException>(() => new ProductVariant(ProductId, price: 1000m, currency: "ARS", oldPrice: 900m));
    }

    [Fact]
    public void Constructor_WithOldPriceGreaterThanPrice_Succeeds()
    {
        var variant = new ProductVariant(ProductId, price: 1000m, currency: "ARS", oldPrice: 1200m);

        Assert.Equal(1200m, variant.OldPrice);
    }

    [Fact]
    public void Constructor_WithNonArsCurrency_Throws()
    {
        Assert.Throws<DomainException>(() => new ProductVariant(ProductId, price: 1000m, currency: "USD"));
    }
}
