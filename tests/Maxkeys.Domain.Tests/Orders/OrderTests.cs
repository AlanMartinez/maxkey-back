using Maxkeys.Domain.Common;
using Maxkeys.Domain.Orders;

namespace Maxkeys.Domain.Tests.Orders;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static OrderLine ValidLine(decimal unitPrice = 5000m, int quantity = 1) =>
        new(Guid.NewGuid(), "FC Points", "PS5 Standard", unitPrice, quantity);

    private static Order CreatePendingOrder() =>
        Order.Create(null, "buyer@example.com", new[] { ValidLine(unitPrice: 5000m, quantity: 2), ValidLine(unitPrice: 1500m, quantity: 1) }, Now);

    [Fact]
    public void Create_WithValidLines_ComputesTotalAndKeepsSnapshots()
    {
        var order = CreatePendingOrder();
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(11500m, order.TotalAmount);
        Assert.Equal(2, order.Items.Count);
    }

    [Fact]
    public void Create_With21Items_Throws() =>
        Assert.Throws<DomainException>(() =>
            Order.Create(null, "buyer@example.com", Enumerable.Range(0, 21).Select(_ => ValidLine()).ToList(), Now));

    [Fact]
    public void Create_WithNoItems_Throws() =>
        Assert.Throws<DomainException>(() => Order.Create(null, "buyer@example.com", Array.Empty<OrderLine>(), Now));

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Create_WithInvalidEmail_Throws(string email) =>
        Assert.Throws<DomainException>(() => Order.Create(null, email, new[] { ValidLine() }, Now));

    [Fact]
    public void AttachPreference_WhenPending_SetsPreferenceId()
    {
        var order = CreatePendingOrder();
        order.AttachPreference("pref-123");
        Assert.Equal("pref-123", order.MpPreferenceId);
    }

    [Fact]
    public void AttachPreference_WhenAlreadyAttached_Throws()
    {
        var order = CreatePendingOrder();
        order.AttachPreference("pref-123");
        Assert.Throws<DomainConflictException>(() => order.AttachPreference("pref-456"));
    }

    [Fact]
    public void RecordPaymentAttempt_WhenPending_SetsFieldsAndKeepsPending()
    {
        var order = CreatePendingOrder();
        order.RecordPaymentAttempt("pay-1", "rejected", Now.AddMinutes(5));
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal("pay-1", order.LastPaymentAttemptId);
        Assert.Equal("rejected", order.LastPaymentAttemptStatus);
        Assert.Equal(Now.AddMinutes(5), order.LastPaymentAttemptAt);
        Assert.Equal(Now.AddMinutes(5), order.UpdatedAt);
    }

    [Fact]
    public void RecordPaymentAttempt_WhenNotPending_Throws()
    {
        var order = CreatePendingOrder();
        order.MarkPaid("pay-1", Now.AddMinutes(1));
        Assert.Throws<DomainConflictException>(() => order.RecordPaymentAttempt("pay-2", "rejected", Now.AddMinutes(2)));
    }

    [Fact]
    public void MarkPaid_WhenPending_SetsPaidFieldsAndApprovedAttemptStatus()
    {
        var order = CreatePendingOrder();
        order.MarkPaid("pay-1", Now.AddMinutes(2));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal("pay-1", order.MpPaymentId);
        Assert.Equal(Now.AddMinutes(2), order.PaidAt);
        Assert.Equal("approved", order.LastPaymentAttemptStatus);
    }

    [Fact]
    public void MarkPaid_WhenNotPending_Throws()
    {
        var order = CreatePendingOrder();
        order.MarkPaid("pay-1", Now.AddMinutes(1));
        Assert.Throws<DomainConflictException>(() => order.MarkPaid("pay-2", Now.AddMinutes(2)));
    }

    [Fact]
    public void MarkAwaitingFulfillment_WhenPaid_SetsStatus()
    {
        var order = CreatePendingOrder();
        order.MarkPaid("pay-1", Now.AddMinutes(1));
        order.MarkAwaitingFulfillment(Now.AddMinutes(2));
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
    }

    [Fact]
    public void MarkAwaitingFulfillment_WhenNotPaid_Throws()
    {
        var order = CreatePendingOrder();
        Assert.Throws<DomainConflictException>(() => order.MarkAwaitingFulfillment(Now.AddMinutes(1)));
    }

    [Fact]
    public void Cancel_WhenPending_SetsCancelled()
    {
        var order = CreatePendingOrder();
        order.Cancel(Now.AddMinutes(1));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_WhenNotPending_Throws()
    {
        var order = CreatePendingOrder();
        order.MarkPaid("pay-1", Now.AddMinutes(1));
        Assert.Throws<DomainConflictException>(() => order.Cancel(Now.AddMinutes(2)));
    }
}
