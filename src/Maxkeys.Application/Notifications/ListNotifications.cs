using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Notifications;

public sealed class ListNotifications
{
    private readonly IAppDbContext _db;

    public ListNotifications(IAppDbContext db)
    {
        _db = db;
    }

    public Task<List<NotificationSummary>> ExecuteAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId && notification.ReadAt == null)
            .OrderByDescending(notification => notification.CreatedAt)
            .Select(notification => new NotificationSummary(notification.Id, notification.Type, notification.ReadAt))
            .ToListAsync(cancellationToken);
}

public sealed record NotificationSummary(Guid Id, string Type, DateTime? ReadAt);
