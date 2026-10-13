using Microsoft.Extensions.Options;

namespace Cichlids.Infrastructure.Outbox;

/// <summary>
/// Rejects outbox dispatcher settings whose durations or batch size are not positive or whose
/// reservation lease is shorter than three delivery timeouts, so a misconfigured dispatcher fails
/// the startup of the host.
/// </summary>
public sealed class OutboxDispatcherOptionsValidator : IValidateOptions<OutboxDispatcherOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxDispatcherOptions options)
    {
        var failures = new List<string>();

        AddIfNotPositive(failures, nameof(options.PollInterval), options.PollInterval);
        AddIfNotPositive(failures, nameof(options.MaxBackoff), options.MaxBackoff);
        AddIfNotPositive(failures, nameof(options.GiveUpAfter), options.GiveUpAfter);
        AddIfNotPositive(failures, nameof(options.ReservationLease), options.ReservationLease);
        AddIfNotPositive(failures, nameof(options.DeliveryTimeout), options.DeliveryTimeout);

        // One delivery plus a margin of two timeouts for reserving and recording has to fit into a
        // reservation, so every pass makes progress on at least one subscriber.
        if (options.ReservationLease < options.DeliveryTimeout * 3)
        {
            failures.Add(
                $"{OutboxDispatcherOptions.SectionName}:{nameof(options.ReservationLease)} must be at least three times "
                + $"{nameof(options.DeliveryTimeout)}.");
        }

        if (options.BatchSize <= 0)
        {
            failures.Add($"{OutboxDispatcherOptions.SectionName}:{nameof(options.BatchSize)} must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddIfNotPositive(List<string> failures, string setting, TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            failures.Add($"{OutboxDispatcherOptions.SectionName}:{setting} must be positive.");
        }
    }
}
