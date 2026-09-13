using Maxkeys.Application.Notifications;
using Maxkeys.Application.Outbox;
using Maxkeys.Application.Tests.Fakes;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Orders;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Outbox;

/// <summary>
/// Covers <see cref="OrderApprovedHandler"/> (outbox-processing spec:
/// OrderApproved Handler; tasks.md 7.6): <c>Paid</c> → <c>AwaitingFulfillment</c>
/// with exactly one operator email; already-transitioned orders are a no-op;
/// an unknown order throws so the outbox processor can retry/dead-letter.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderApprovedHandlerTests
{
    private const string OperatorAddress = "ops@maxkeys.test";

    private readonly PostgresFixture _fixture;

    public OrderApprovedHandlerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Paid_order_transitions_to_awaiting_fulfillment_and_sends_one_operator_email()
    {
        var orderId = await SeedPaidOrderAsync();
        var emailSender = new RecordingEmailSender();

        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await sut.HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await context.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);

        var email = Assert.Single(emailSender.SentMessages);
        Assert.Equal(OperatorAddress, email.To);
        Assert.Contains(orderId.ToString(), email.Subject + email.TextBody);
        Assert.DoesNotContain("EncryptedCode", email.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nonce", email.TextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Already_awaiting_fulfillment_is_a_noop_with_no_email()
    {
        var orderId = await SeedPaidOrderAsync();
        var emailSender = new RecordingEmailSender();

        await using var firstContext = _fixture.CreateContext();
        await CreateHandler(firstContext, emailSender).HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);
        Assert.Single(emailSender.SentMessages);

        await using var secondContext = _fixture.CreateContext();
        await CreateHandler(secondContext, emailSender).HandleAsync(OrderApprovedEvent(orderId), CancellationToken.None);

        var order = await secondContext.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.AwaitingFulfillment, order.Status);
        Assert.Single(emailSender.SentMessages);
    }

    [Fact]
    public async Task Unknown_order_throws()
    {
        var emailSender = new RecordingEmailSender();
        await using var context = _fixture.CreateContext();
        var sut = CreateHandler(context, emailSender);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.HandleAsync(OrderApprovedEvent(Guid.NewGuid()), CancellationToken.None));

        Assert.Empty(emailSender.SentMessages);
    }

    private static OrderApprovedHandler CreateHandler(AppDbContext context, RecordingEmailSender emailSender)
    {
        var options = Options.Create(new EmailOptions { OperatorTo = OperatorAddress });
        return new OrderApprovedHandler(context, emailSender, options, NullLogger<OrderApprovedHandler>.Instance);
    }

    private static OutboxEvent OrderApprovedEvent(Guid orderId) =>
        new(OutboxEventTypes.OrderApproved, $$"""{"orderId":"{{orderId}}"}""", DateTimeOffset.UtcNow);

    private async Task<Guid> SeedPaidOrderAsync()
    {
        await using var context = _fixture.CreateContext();
        var order = Order.Create(
            null,
            $"buyer-{Guid.NewGuid():N}@example.com",
            [new OrderLine(Guid.NewGuid(), "Product", "Standard", 2_500m, 2)],
            DateTimeOffset.UtcNow);
        order.MarkPaid($"pay-{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
}
