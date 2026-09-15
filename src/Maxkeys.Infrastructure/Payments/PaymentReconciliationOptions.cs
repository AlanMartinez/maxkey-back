using Microsoft.Extensions.Options;

namespace Maxkeys.Infrastructure.Payments;

/// <summary>Binds the <c>PaymentReconciliation</c> config section (design section 10): fallback-reconciliation cadence and staleness threshold.</summary>
public sealed class PaymentReconciliationOptions
{
    public const string SectionName = "PaymentReconciliation";

    public int IntervalMinutes { get; set; } = 10;

    public int StaleAfterMinutes { get; set; } = 15;
}

/// <summary>Fails DI options validation when any <see cref="PaymentReconciliationOptions"/> value is below its minimum of 1.</summary>
public sealed class PaymentReconciliationOptionsValidator : IValidateOptions<PaymentReconciliationOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentReconciliationOptions options)
    {
        if (options.IntervalMinutes < 1)
        {
            return ValidateOptionsResult.Fail($"{PaymentReconciliationOptions.SectionName}:{nameof(PaymentReconciliationOptions.IntervalMinutes)} must be >= 1.");
        }

        if (options.StaleAfterMinutes < 1)
        {
            return ValidateOptionsResult.Fail($"{PaymentReconciliationOptions.SectionName}:{nameof(PaymentReconciliationOptions.StaleAfterMinutes)} must be >= 1.");
        }

        return ValidateOptionsResult.Success;
    }
}
