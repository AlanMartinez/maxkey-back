using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Endpoints;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Domain.Notifications;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Notifications;

[Collection(Hs256ApiCollection.Name)]
public sealed class NotificationEndpointsTests
{
    private readonly Hs256ApiTestFixture _factory;

    public NotificationEndpointsTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_returns_only_caller_unread_notifications_newest_first_and_no_keys()
    {
        var caller = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var older = await SeedNotificationAsync(caller, DateTime.UtcNow.AddMinutes(-2));
        var newer = await SeedNotificationAsync(caller, DateTime.UtcNow.AddMinutes(-1));
        await SeedNotificationAsync(otherUser, DateTime.UtcNow);
        await SeedNotificationAsync(caller, DateTime.UtcNow, readAt: DateTime.UtcNow);

        var response = await UserClient(caller).GetAsync("/me/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("key", rawJson, StringComparison.OrdinalIgnoreCase);
        var notifications = Assert.IsType<List<NotificationResponse>>(
            await response.Content.ReadFromJsonAsync<List<NotificationResponse>>());
        Assert.Equal([newer, older], notifications.Select(notification => notification.Id));
        Assert.All(notifications, notification =>
        {
            Assert.Equal(NotificationTypes.OrderDelivered, notification.Type);
            Assert.Null(notification.ReadAt);
        });
    }

    [Fact]
    public async Task Read_is_owner_scoped_and_idempotent()
    {
        var owner = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var notificationId = await SeedNotificationAsync(owner, DateTime.UtcNow);

        var forbiddenResponse = await UserClient(otherUser).PostAsync($"/me/notifications/{notificationId}/read", content: null);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenResponse.StatusCode);

        var missingResponse = await UserClient(owner).PostAsync($"/me/notifications/{Guid.NewGuid()}/read", content: null);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        var firstResponse = await UserClient(owner).PostAsync($"/me/notifications/{notificationId}/read", content: null);
        var secondResponse = await UserClient(owner).PostAsync($"/me/notifications/{notificationId}/read", content: null);

        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondResponse.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = await db.Notifications.SingleAsync(notification => notification.Id == notificationId);
        Assert.NotNull(notification.ReadAt);
    }

    private async Task<Guid> SeedNotificationAsync(Guid userId, DateTime createdAt, DateTime? readAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = new Notification(userId, Guid.NewGuid(), createdAt);
        if (readAt is not null)
        {
            notification.MarkRead(readAt.Value);
        }

        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification.Id;
    }

    private HttpClient UserClient(Guid userId)
    {
        var token = TestTokens.CreateHs256(
            userId.ToString(), Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
