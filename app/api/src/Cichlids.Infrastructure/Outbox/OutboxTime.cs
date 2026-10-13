namespace Cichlids.Infrastructure.Outbox;

/// <summary>
/// The current time for outbox bookkeeping, in UTC and at the microsecond precision Postgres
/// stores, so a time value read back from the database equals the value that was written.
/// </summary>
internal static class OutboxTime
{
    public static DateTimeOffset UtcNow(TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();
        return new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
    }
}
