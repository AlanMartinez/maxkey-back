using Maxkeys.Domain.Carousel;
using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Tests.Carousel;

public class CarouselSlideTests
{
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public void Constructor_WithValidData_CreatesActiveSlide()
    {
        var slide = new CarouselSlide(ProductId, sortOrder: 1);

        Assert.Equal(ProductId, slide.ProductId);
        Assert.Equal(1, slide.SortOrder);
        Assert.True(slide.IsActive);
        Assert.Null(slide.Title);
    }

    [Fact]
    public void Constructor_WithEmptyProductId_Throws()
    {
        Assert.Throws<DomainException>(() => new CarouselSlide(Guid.Empty, sortOrder: 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_WithNegativeSortOrder_Throws(int sortOrder)
    {
        Assert.Throws<DomainException>(() => new CarouselSlide(ProductId, sortOrder));
    }

    [Fact]
    public void Constructor_WithWhitespaceOverrides_NormalizesToNull()
    {
        var slide = new CarouselSlide(ProductId, sortOrder: 0, title: "   ", caption: "", imageKey: "  ");

        Assert.Null(slide.Title);
        Assert.Null(slide.Caption);
        Assert.Null(slide.ImageKey);
    }

    [Fact]
    public void Constructor_WithPaddedOverrides_TrimsWhitespace()
    {
        var slide = new CarouselSlide(ProductId, sortOrder: 0, title: "  Hero  ");

        Assert.Equal("Hero", slide.Title);
    }

    [Fact]
    public void Update_WithValidData_ReplacesAllFields()
    {
        var slide = new CarouselSlide(ProductId, sortOrder: 0);
        var newProductId = Guid.NewGuid();

        slide.Update(newProductId, sortOrder: 2, isActive: false, title: "New Title", caption: "New Caption", imageKey: "products/new.png");

        Assert.Equal(newProductId, slide.ProductId);
        Assert.Equal(2, slide.SortOrder);
        Assert.False(slide.IsActive);
        Assert.Equal("New Title", slide.Title);
        Assert.Equal("New Caption", slide.Caption);
        Assert.Equal("products/new.png", slide.ImageKey);
    }

    [Fact]
    public void Update_WithEmptyProductId_Throws()
    {
        var slide = new CarouselSlide(ProductId, sortOrder: 0);

        Assert.Throws<DomainException>(() => slide.Update(Guid.Empty, sortOrder: 0, isActive: true, null, null, null));
    }

    [Fact]
    public void Update_WithNegativeSortOrder_Throws()
    {
        var slide = new CarouselSlide(ProductId, sortOrder: 0);

        Assert.Throws<DomainException>(() => slide.Update(ProductId, sortOrder: -1, isActive: true, null, null, null));
    }
}
