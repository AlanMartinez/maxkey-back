using Maxkeys.Application.Outbox;
using Maxkeys.Domain.Outbox;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxkeys.Infrastructure.Outbox;

/// <summary>
/// Polls due <see cref="OutboxEvent"/> rows and dispatches each to its <see cref="IOutboxHandler"/> by
/// <see cref="OutboxEvent.Type"/> (design section 6c), multi-instance safe via <see cref="OutboxClaimQuery"/>'s
/// <c>SKIP LOCKED</c> claim. Each event saves individually so one failure never rolls back a sibling.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxClaimQuery _claimQuery;
    private readonly IOptions<OutboxOptions> _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory, OutboxClaimQuery claimQuery, IOptions<OutboxOptions> options, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _claimQuery = claimQuery;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimedFullBatch = false;
            try
            {
                claimedFullBatch = await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox processor poll cycle failed unexpectedly; retrying after the next poll interval.");
            }

            if (claimedFullBatch) continue; // more due rows are likely waiting — re-poll immediately

            try { await Task.Delay(pollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One claim-and-dispatch pass. <c>internal</c>+<c>InternalsVisibleTo</c> lets tests drive it directly instead of sleeping through the loop.</summary>
    internal async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handlersByType = scope.ServiceProvider.GetServices<IOutboxHandler>().ToDictionary(h => h.EventType, StringComparer.Ordinal);

        var claimed = await _claimQuery.ClaimBatchAsync(
            db, DateTimeOffset.UtcNow, opts.BatchSize, TimeSpan.FromSeconds(opts.LeaseSeconds), cancellationToken);

        foreach (var evt in claimed)
        {
            await DispatchAsync(db, evt, handlersByType, opts.MaxAttempts, cancellationToken);
        }

        return claimed.Count >= opts.BatchSize;
    }

    private async Task DispatchAsync(
        AppDbContext db, OutboxEvent evt, IReadOnlyDictionary<string, IOutboxHandler> handlersByType, int maxAttempts, CancellationToken ct)
    {
        try
        {
            if (!handlersByType.TryGetValue(evt.Type, out var handler))
            {
                throw new InvalidOperationException($"No IOutboxHandler registered for outbox event type '{evt.Type}'.");
            }

            await handler.HandleAsync(evt, ct);
            evt.MarkProcessed(DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            evt.MarkFailedAttempt(Truncate(ex.Message, 500), DateTimeOffset.UtcNow, maxAttempts);
            _logger.Log(evt.Status == OutboxEventStatus.Failed ? LogLevel.Error : LogLevel.Warning, ex,
                "Outbox event {EventId} ({EventType}) attempt {Attempts} failed; status now {Status}, next attempt {NextAttemptAt}.",
                evt.Id, evt.Type, evt.Attempts, evt.Status, evt.NextAttemptAt);
        }

        await db.SaveChangesAsync(ct); // per event, not batched — one failure never rolls back a sibling
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>Registers <see cref="OutboxOptions"/>, its validator, <see cref="OutboxClaimQuery"/>, and the <see cref="OutboxProcessor"/> hosted service.</summary>
public static class OutboxProcessorServiceCollectionExtensions
{
    public static IServiceCollection AddOutboxProcessor(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>();
        services.AddSingleton<OutboxClaimQuery>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
