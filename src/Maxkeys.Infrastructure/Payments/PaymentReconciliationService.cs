using Maxkeys.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxkeys.Infrastructure.Payments;

/// <summary>
/// Slow-timer wrapper around <see cref="ReconcileStalePayments"/> — polls every
/// <see cref="PaymentReconciliationOptions.IntervalMinutes"/> for orders stuck
/// <c>Pending</c> past <see cref="PaymentReconciliationOptions.StaleAfterMinutes"/> and
/// replays any MP-approved-but-unwebhooked payment through the normal webhook pipeline.
/// One failed cycle never stops the loop — logged and retried next interval.
/// </summary>
public sealed class PaymentReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PaymentReconciliationOptions> _options;
    private readonly ILogger<PaymentReconciliationService> _logger;

    public PaymentReconciliationService(
        IServiceScopeFactory scopeFactory, IOptions<PaymentReconciliationOptions> options, ILogger<PaymentReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.Value.IntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Payment reconciliation cycle failed unexpectedly; retrying after the next interval.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary><c>internal</c>+<c>InternalsVisibleTo</c> lets tests drive one pass directly instead of sleeping through the loop.</summary>
    internal async Task<ReconcileStalePaymentsResult> ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        var staleAfter = TimeSpan.FromMinutes(_options.Value.StaleAfterMinutes);

        using var scope = _scopeFactory.CreateScope();
        var reconcile = scope.ServiceProvider.GetRequiredService<ReconcileStalePayments>();
        var result = await reconcile.ExecuteAsync(staleAfter, DateTimeOffset.UtcNow, cancellationToken);

        if (result.ScannedCount > 0)
        {
            _logger.LogInformation(
                "Payment reconciliation: {ScannedCount} stale order(s) checked, {ReconciledCount} reconciled, {StillPendingCount} still pending.",
                result.ScannedCount, result.ReconciledCount, result.StillPendingCount);
        }

        return result;
    }
}

/// <summary>Registers <see cref="PaymentReconciliationOptions"/>, its validator, and the <see cref="PaymentReconciliationService"/> hosted service.</summary>
public static class PaymentReconciliationServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentReconciliation(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddOptions<PaymentReconciliationOptions>().Bind(configuration.GetSection(PaymentReconciliationOptions.SectionName));
        services.AddSingleton<IValidateOptions<PaymentReconciliationOptions>, PaymentReconciliationOptionsValidator>();
        services.AddScoped<ReconcileStalePayments>();
        services.AddHostedService<PaymentReconciliationService>();

        return services;
    }
}
