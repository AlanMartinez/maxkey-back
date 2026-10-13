using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Domain.Tests.Orders;

/// <summary>Bank-transfer order creation and the admin approve/reject transitions.</summary>
public class OrderBankTransferTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static OrderLine ValidLine() => new(Guid.NewGuid(), "FC Points", "PS5 Standard", 5000m, 1);

    private static Order CreateTransferOrder(string? phone = "+54 9 11 1234-5678") =>
        Order.Create(null, "buyer@example.com", new[] { ValidLine() }, Now, PaymentMethod.BankTransfer, phone);

    [Fact]
    public void Create_DefaultsToMercadoPagoPendingWithoutPhone()
    {
        var order = Order.Create(null, "buyer@example.com", new[] { ValidLine() }, Now);

        Assert.Equal(PaymentMethod.MercadoPago, order.PaymentMethod);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.BuyerPhone);
    }

    [Fact]
    public void Create_MercadoPago_IgnoresPhone()
    {
        var order = Order.Create(null, "buyer@example.com", new[] { ValidLine() }, Now, PaymentMethod.MercadoPago, "not a phone");

        Assert.Null(order.BuyerPhone);
    }

    [Fact]
    public void Create_BankTransfer_AwaitsValidationWithNormalizedPhone()
    {
        var order = CreateTransferOrder();

        Assert.Equal(PaymentMethod.BankTransfer, order.PaymentMethod);
        Assert.Equal(OrderStatus.AwaitingTransferValidation, order.Status);
        Assert.Equal("+5491112345678", order.BuyerPhone);
        Assert.Null(order.PaidAt);
    }

    [Theory]
    [InlineData("11 1234-5678", "1112345678")]
    [InlineData("(011) 4567-8901", "01145678901")]
    [InlineData("+541112345678", "+541112345678")]
    [InlineData("12345678", "12345678")]
    [InlineData("123456789012345", "123456789012345")]
    public void NormalizePhone_StripsSeparatorsAndKeepsLeadingPlus(string input, string expected) =>
        Assert.Equal(expected, Order.NormalizePhone(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234567")]
    [InlineData("1234567890123456")]
    [InlineData("11.1234.5678")]
    [InlineData("11 1234 abcd")]
    [InlineData("54+1112345678")]
    [InlineData("++541112345678")]
    public void Create_BankTransfer_WithInvalidPhone_ThrowsValidationOnPhone(string? phone)
    {
        var exception = Assert.Throws<DomainValidationException>(() => CreateTransferOrder(phone));

        Assert.Equal("phone", exception.Field);
    }

    [Fact]
    public void ApproveTransfer_WhenAwaitingValidation_MarksPaidWithoutPaymentId()
    {
        var order = CreateTransferOrder();

        order.ApproveTransfer(Now.AddHours(1));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(Now.AddHours(1), order.PaidAt);
        Assert.Equal(Now.AddHours(1), order.UpdatedAt);
        Assert.Null(order.MpPaymentId);
    }

    [Fact]
    public void ApprovedTransfer_ContinuesThroughFulfillmentLikeAnMpOrder()
    {
        var order = CreateTransferOrder();
        order.ApproveTransfer(Now.AddHours(1));

        order.MarkAwaitingFulfillment(Now.AddHours(2));

        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
    }

    [Fact]
    public void RejectTransfer_WhenAwaitingValidation_Cancels()
    {
        var order = CreateTransferOrder();

        order.RejectTransfer(Now.AddHours(1));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Now.AddHours(1), order.UpdatedAt);
        Assert.Null(order.PaidAt);
    }

    [Fact]
    public void ApproveAndReject_WhenAlreadyReviewed_Throw()
    {
        var approved = CreateTransferOrder();
        approved.ApproveTransfer(Now.AddHours(1));
        var rejected = CreateTransferOrder();
        rejected.RejectTransfer(Now.AddHours(1));

        Assert.Throws<DomainConflictException>(() => approved.ApproveTransfer(Now.AddHours(2)));
        Assert.Throws<DomainConflictException>(() => approved.RejectTransfer(Now.AddHours(2)));
        Assert.Throws<DomainConflictException>(() => rejected.ApproveTransfer(Now.AddHours(2)));
        Assert.Throws<DomainConflictException>(() => rejected.RejectTransfer(Now.AddHours(2)));
    }

    [Fact]
    public void ApproveAndReject_OnMercadoPagoOrder_Throw()
    {
        var order = Order.Create(null, "buyer@example.com", new[] { ValidLine() }, Now);

        Assert.Throws<DomainConflictException>(() => order.ApproveTransfer(Now.AddHours(1)));
        Assert.Throws<DomainConflictException>(() => order.RejectTransfer(Now.AddHours(1)));
    }

    [Fact]
    public void MercadoPagoOnlyMethods_RejectTransferOrders()
    {
        var order = CreateTransferOrder();

        Assert.Throws<DomainConflictException>(() => order.AttachPreference("pref-1"));
        Assert.Throws<DomainConflictException>(() => order.RecordPaymentAttempt("pay-1", "rejected", Now.AddMinutes(1)));
        Assert.Throws<DomainConflictException>(() => order.MarkPaid("pay-1", Now.AddMinutes(1)));
        Assert.Throws<DomainConflictException>(() => order.Cancel(Now.AddMinutes(1)));
    }
}
