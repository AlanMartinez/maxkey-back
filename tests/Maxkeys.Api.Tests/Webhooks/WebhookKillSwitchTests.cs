using System.Net;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Webhooks;

/// <summary>
/// <c>Payments:WebhookEnabled=false</c> must short-circuit before signature or
/// dedupe checks, with zero I/O (payments-webhook spec "Kill Switch").
/// </summary>
[Collection(WebhookDisabledApiCollection.Name)]
public sealed class WebhookKillSwitchTests
{
    private readonly WebhookDisabledApiTestFixture _factory;

    public WebhookKillSwitchTests(WebhookDisabledApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Disabled_webhook_returns_503_even_for_a_well_formed_request_with_no_state_change()
    {
        const string requestId = "kill-switch-request-1";
        var client = _factory.CreateClient();

        // Even a garbage/absent signature must never be evaluated once the kill switch is on.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/mercadopago?data.id=1&type=payment");
        request.Headers.Add("x-signature", "ts=1704908010,v1=garbage");
        request.Headers.Add("x-request-id", requestId);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.ProcessedWebhookNotifications.AnyAsync(n => n.RequestId == requestId));
    }
}
