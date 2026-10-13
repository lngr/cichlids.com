using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Outbox;

public enum OutboxRedeliveryResult
{
    Scheduled,
    NotFound,
    NotGivenUp,
}

/// <summary>
/// Schedules given-up outbox events for redelivery. A redelivered event is open and due at once,
/// starts a new delivery window with no failed attempts, and keeps its id and its acknowledged
/// subscriptions, so only subscribers that have not acknowledged it receive it.
/// </summary>
public sealed class OutboxRedelivery(CichlidsDbContext context, TimeProvider timeProvider)
{
    public async Task<OutboxRedeliveryResult> RedeliverAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var scheduled = await ScheduleAsync(context.OutboxEvents.Where(e => e.Id == eventId), cancellationToken);
        if (scheduled == 1)
        {
            return OutboxRedeliveryResult.Scheduled;
        }

        return await context.OutboxEvents.AnyAsync(e => e.Id == eventId, cancellationToken)
            ? OutboxRedeliveryResult.NotGivenUp
            : OutboxRedeliveryResult.NotFound;
    }

    /// <summary>
    /// Schedules every given-up event and returns how many were scheduled.
    /// </summary>
    public Task<int> RedeliverAllGivenUpAsync(CancellationToken cancellationToken) =>
        ScheduleAsync(context.OutboxEvents, cancellationToken);

    private Task<int> ScheduleAsync(IQueryable<OutboxEvent> events, CancellationToken cancellationToken)
    {
        var now = OutboxTime.UtcNow(timeProvider);

        return events
            .Where(e => e.GivenUpAt != null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.GivenUpAt, (DateTimeOffset?)null)
                    .SetProperty(e => e.AttemptCount, 0)
                    .SetProperty(e => e.LastError, (string?)null)
                    .SetProperty(e => e.NextAttemptAt, now)
                    .SetProperty(e => e.RedeliveredAt, now),
                cancellationToken);
    }
}
