using Maxkeys.Domain.Common;
using Maxkeys.Domain.Payments;

namespace Maxkeys.Domain.Tests.Payments;

public class ProcessedWebhookNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_WithValidData_RecordsFields()
    {
        var orderId = Guid.NewGuid();

        var notification = new ProcessedWebhookNotification("req-1", "payment-1", orderId, Now);

        Assert.Equal("req-1", notification.RequestId);
        Assert.Equal(orderId, notification.OrderId);
        Assert.Equal(Now, notification.ReceivedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyRequestId_Throws(string requestId)
    {
        Assert.Throws<DomainException>(() => new ProcessedWebhookNotification(requestId, "payment-1", null, Now));
    }
}
