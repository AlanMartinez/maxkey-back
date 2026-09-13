using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Domain.Tests.Orders;

public class OrderItemTests
{
    [Fact]
    public void Constructor_WithValidData_SetsFields()
    {
        var variantId = Guid.NewGuid();
        var item = new OrderItem(variantId, "FC Points", "PS5 Standard", 5000m, 2);
        Assert.Equal(variantId, item.ProductVariantId);
        Assert.Equal("FC Points", item.ProductNameSnapshot);
        Assert.Equal("PS5 Standard", item.VariantNameSnapshot);
        Assert.Equal(2, item.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Constructor_WithQuantityOutOfRange_Throws(int quantity) =>
        Assert.Throws<DomainException>(() => new OrderItem(Guid.NewGuid(), "FC Points", "PS5 Standard", 5000m, quantity));

    [Fact]
    public void Constructor_WithEmptyProductNameSnapshot_Throws() =>
        Assert.Throws<DomainException>(() => new OrderItem(Guid.NewGuid(), "", "PS5 Standard", 5000m, 1));

    [Fact]
    public void Constructor_WithEmptyVariantNameSnapshot_Throws() =>
        Assert.Throws<DomainException>(() => new OrderItem(Guid.NewGuid(), "FC Points", "", 5000m, 1));
}
