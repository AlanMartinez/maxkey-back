using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Tests.Catalog;

public class ProductImageTests
{
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public void Constructor_WithValidData_CreatesImage()
    {
        var image = new ProductImage(ProductId, "products/gallery/1.png", sortOrder: 0);

        Assert.Equal(ProductId, image.ProductId);
        Assert.Equal("products/gallery/1.png", image.ImageKey);
        Assert.Equal(0, image.SortOrder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WithEmptyImageKey_Throws(string? imageKey)
    {
        Assert.Throws<DomainException>(() => new ProductImage(ProductId, imageKey!, sortOrder: 0));
    }
}
