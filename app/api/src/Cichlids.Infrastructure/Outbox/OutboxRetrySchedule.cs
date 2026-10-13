namespace Cichlids.Infrastructure.Outbox;

/// <summary>
/// Decides when a failed outbox event is attempted next and whether it is given up. The delay
/// counts from the failed attempt and doubles with each failed attempt up to the configured
/// ceiling. The event is given up only once the configured period has passed since the start of
/// its delivery window, which is the time the event occurred or, after an administrator
/// redelivery, the time of that redelivery. The number of attempts never decides the give-up, so
/// an outage shorter than the period loses no event.
/// </summary>
public static class OutboxRetrySchedule
{
    public static (DateTimeOffset NextAttemptAt, bool GivenUp) AfterFailure(
        int attemptCount, DateTimeOffset attemptedAt, DateTimeOffset windowStart, OutboxDispatcherOptions options)
    {
        var nextAttemptAt = attemptedAt + OutboxBackoff.Compute(attemptCount, options.MaxBackoff);
        var givenUp = attemptedAt - windowStart >= options.GiveUpAfter;
        return (nextAttemptAt, givenUp);
    }
}
