using Maxkeys.Application.Outbox;
using Maxkeys.Domain.Outbox;

namespace Maxkeys.Application.Tests.Fakes;

/// <summary>Configurable <see cref="IOutboxHandler"/> for <c>OutboxProcessor</c> tests — records handled events, or throws a configured exception.</summary>
public sealed class FakeOutboxHandler : IOutboxHandler
{
    public required string EventType { get; init; }

    /// <summary>When set, <see cref="HandleAsync"/> throws this instead of succeeding.</summary>
    public Exception? ExceptionToThrow { get; set; }

    public List<OutboxEvent> HandledEvents { get; } = [];

    public Task HandleAsync(OutboxEvent evt, CancellationToken cancellationToken)
    {
        if (ExceptionToThrow is not null) throw ExceptionToThrow;
        HandledEvents.Add(evt);
        return Task.CompletedTask;
    }
}
