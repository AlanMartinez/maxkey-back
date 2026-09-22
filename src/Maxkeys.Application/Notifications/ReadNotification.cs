using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Notifications;

public sealed class ReadNotification
{
    private readonly IAppDbContext _db;
    private readonly Func<DateTime> _utcNow;

    public ReadNotification(IAppDbContext db, Func<DateTime>? utcNow = null)
    {
        _db = db;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<bool> ExecuteAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var markedRead = await _db.Notifications
            .Where(notification =>
                notification.Id == notificationId &&
                notification.UserId == userId &&
                notification.ReadAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.ReadAt, _utcNow()),
                cancellationToken);
        if (markedRead > 0)
        {
            return true;
        }

        return await _db.Notifications.AnyAsync(
            notification => notification.Id == notificationId && notification.UserId == userId,
            cancellationToken);
    }
}
