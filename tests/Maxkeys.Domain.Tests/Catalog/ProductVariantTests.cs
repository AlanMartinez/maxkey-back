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

    [Fact]
    public void Constructor_CreatesVariantThatIsNotRecommended()
    {
        var variant = new ProductVariant(ProductId, price: 1000m, currency: "ARS");

        Assert.False(variant.IsRecommended);
    }

    [Fact]
    public void SetRecommended_RoundTripsTheFlag()
    {
        var variant = new ProductVariant(ProductId, price: 1000m, currency: "ARS");

        variant.SetRecommended(true);
        Assert.True(variant.IsRecommended);

        variant.SetRecommended(false);
        Assert.False(variant.IsRecommended);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositivePrice_Throws(decimal price)
    {
        Assert.Throws<DomainException>(() => new ProductVariant(ProductId, price, "ARS"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-5)]
    [InlineData(150)]
    public void Constructor_WithOutOfRangeDiscountPercentage_Throws(decimal discountPercentage)
    {
        Assert.Throws<DomainException>(() => new ProductVariant(ProductId, price: 1000m, currency: "ARS", discountPercentage: discountPercentage));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(99)]
    public void Constructor_WithInRangeDiscountPercentage_Succeeds(decimal discountPercentage)
    {
        var variant = new ProductVariant(ProductId, price: 1000m, currency: "ARS", discountPercentage: discountPercentage);

        Assert.Equal(discountPercentage, variant.DiscountPercentage);
    }

    [Fact]
    public void Constructor_WithUsdCurrency_Succeeds()
    {
        var variant = new ProductVariant(ProductId, price: 1000m, currency: "USD");

        Assert.Equal("USD", variant.Currency);
    }

    [Fact]
    public void Constructor_WithNonWhitelistedCurrency_Throws()
    {
        Assert.Throws<DomainException>(() => new ProductVariant(ProductId, price: 1000m, currency: "EUR"));
    }

    [Fact]
    public void UpdateDetails_WithOutOfRangeDiscountPercentage_Throws()
    {
        var variant = new ProductVariant(ProductId, price: 1000m, currency: "ARS");

        Assert.Throws<DomainException>(
            () => variant.UpdateDetails(1000m, discountPercentage: 100m, "ARS", region: null, edition: null, sortOrder: 0, isActive: true));
    }

    [Fact]
    public void UpdateDetails_WithNonWhitelistedCurrency_Throws()
    {
        var variant = new ProductVariant(ProductId, price: 1000m, currency: "ARS");

        Assert.Throws<DomainException>(
            () => variant.UpdateDetails(1000m, discountPercentage: null, "EUR", region: null, edition: null, sortOrder: 0, isActive: true));
    }

    [Fact]
    public void UpdateDetails_WithInRangeDiscountPercentageAndUsdCurrency_Succeeds()
    {
        var variant = new ProductVariant(ProductId, price: 1000m, currency: "ARS");

        variant.UpdateDetails(1000m, discountPercentage: 25m, "USD", region: null, edition: null, sortOrder: 0, isActive: true);

        Assert.Equal(25m, variant.DiscountPercentage);
        Assert.Equal("USD", variant.Currency);
    }
}
