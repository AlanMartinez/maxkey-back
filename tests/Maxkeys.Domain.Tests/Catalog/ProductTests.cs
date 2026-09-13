using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Tests.Catalog;

public class ProductTests
{
    [Fact]
    public void Constructor_WithValidData_CreatesActiveProduct()
    {
        var product = new Product("fc-points", "FC Points", "PS5");

        Assert.Equal("fc-points", product.Slug);
        Assert.True(product.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptySlug_Throws(string slug)
    {
        Assert.Throws<DomainException>(() => new Product(slug, "FC Points", "PS5"));
    }

    [Fact]
    public void Constructor_WithUppercaseSlug_Throws()
    {
        Assert.Throws<DomainException>(() => new Product("FC-Points", "FC Points", "PS5"));
    }

    [Fact]
    public void Constructor_WithEmptyName_Throws()
    {
        Assert.Throws<DomainException>(() => new Product("fc-points", "", "PS5"));
    }

    [Fact]
    public void Constructor_WithEmptyPlatform_Throws()
    {
        Assert.Throws<DomainException>(() => new Product("fc-points", "FC Points", ""));
    }
}
