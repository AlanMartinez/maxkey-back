using Maxkeys.Application.Notifications;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Tests.Notifications;

[Collection(PostgresCollection.Name)]
public sealed class ReadNotificationTests
{
    private readonly PostgresFixture _fixture;

    public ReadNotificationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Concurrent_reads_preserve_one_contender_timestamp()
    {
        var userId = Guid.NewGuid();
        var notificationId = await SeedNotificationAsync(userId);
        var firstReadAt = DateTime.UnixEpoch.AddMinutes(1);
        var secondReadAt = firstReadAt.AddMinutes(1);

        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();
        var firstRead = new ReadNotification(firstContext, () => firstReadAt).ExecuteAsync(notificationId, userId);
        var secondRead = new ReadNotification(secondContext, () => secondReadAt).ExecuteAsync(notificationId, userId);

        var results = await Task.WhenAll(firstRead, secondRead);

        Assert.All(results, Assert.True);
        await using var verifyContext = _fixture.CreateContext();
        var notification = await verifyContext.Notifications.SingleAsync(n => n.Id == notificationId);
        Assert.Contains(notification.ReadAt, new DateTime?[] { firstReadAt, secondReadAt });
    }

    private async Task<Guid> SeedNotificationAsync(Guid userId)
    {
        await using var context = _fixture.CreateContext();
        var notification = new Notification(userId, Guid.NewGuid(), DateTime.UtcNow);
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();
        return notification.Id;
    }
}
