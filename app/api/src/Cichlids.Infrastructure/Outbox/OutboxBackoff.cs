namespace Cichlids.Infrastructure.Outbox;

/// <summary>
/// Computes how long a failed outbox event waits before its next redelivery attempt: doubling
/// from a five-second base with each failed attempt, capped so a stuck subscriber cannot push the
/// wait past the configured ceiling.
/// </summary>
public static class OutboxBackoff
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);

    public static TimeSpan Compute(int attemptCount, TimeSpan maxBackoff)
    {
        if (attemptCount <= 0)
        {
            return TimeSpan.Zero;
        }

        // Compared as a double so attempt counts far beyond the ceiling never overflow a TimeSpan.
        var scaledSeconds = BaseDelay.TotalSeconds * Math.Pow(2, attemptCount - 1);
        return scaledSeconds < maxBackoff.TotalSeconds ? TimeSpan.FromSeconds(scaledSeconds) : maxBackoff;
    }
}
