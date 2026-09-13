using Microsoft.Extensions.Options;

namespace Maxkeys.Infrastructure.Outbox;

/// <summary>Binds the <c>Outbox</c> config section (design section 6c): poll cadence, batch size, lease duration, retry ceiling before dead-letter.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
    public int LeaseSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 8;
}

/// <summary>Fails DI options validation when any <see cref="OutboxOptions"/> value is below its minimum of 1.</summary>
public sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        (string Key, int Value)[] checks =
        [
            (nameof(OutboxOptions.PollIntervalSeconds), options.PollIntervalSeconds),
            (nameof(OutboxOptions.BatchSize), options.BatchSize),
            (nameof(OutboxOptions.LeaseSeconds), options.LeaseSeconds),
            (nameof(OutboxOptions.MaxAttempts), options.MaxAttempts),
        ];

        foreach (var (key, value) in checks)
        {
            if (value < 1) return ValidateOptionsResult.Fail($"Outbox:{key} must be >= 1.");
        }

        return ValidateOptionsResult.Success;
    }
}
