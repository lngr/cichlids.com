using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Infrastructure.Outbox;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Webhooks;

/// <summary>
/// Admin access to given-up outbox events: listing them and triggering their redelivery, one by
/// one or all at once.
/// </summary>
public static class OutboxAdminEndpoints
{
    private const string GivenUpState = "given-up";

    public static IEndpointRouteBuilder MapOutboxAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/outbox")
            .WithTags("Webhooks")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/events", ListEventsAsync).WithName("ListOutboxEvents");
        group.MapPost("/events/{id:guid}/redeliver", RedeliverAsync).WithName("RedeliverOutboxEvent");
        group.MapPost("/given-up/redeliver", RedeliverAllGivenUpAsync).WithName("RedeliverGivenUpOutboxEvents");

        return app;
    }

    /// <summary>
    /// Lists outbox events in the given state, most recently given up first. The only supported
    /// state is "given-up".
    /// </summary>
    private static async Task<Results<Ok<PagedResponse<OutboxEventDto>>, BadRequest<string>>> ListEventsAsync(
        CichlidsDbContext context, string? state, int? offset, int? limit, CancellationToken cancellationToken)
    {
        if (state != GivenUpState)
        {
            return TypedResults.BadRequest($"The state must be '{GivenUpState}'.");
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var givenUp = context.OutboxEvents.AsNoTracking().Where(e => e.GivenUpAt != null);

        var total = await givenUp.CountAsync(cancellationToken);
        var items = await givenUp
            .OrderByDescending(e => e.GivenUpAt)
            .ThenBy(e => e.Id)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(e => new OutboxEventDto(
                e.Id,
                e.EventType,
                e.AggregateType,
                e.AggregateId,
                e.OccurredAt,
                e.AttemptCount,
                e.LastError,
                e.LastAttemptAt,
                e.GivenUpAt,
                e.RedeliveredAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResponse<OutboxEventDto>(total, items));
    }

    private static async Task<Results<Accepted, NotFound, Conflict<string>>> RedeliverAsync(
        OutboxRedelivery redelivery, Guid id, CancellationToken cancellationToken)
    {
        return await redelivery.RedeliverAsync(id, cancellationToken) switch
        {
            OutboxRedeliveryResult.Scheduled => TypedResults.Accepted((string?)null),
            OutboxRedeliveryResult.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Conflict("Only a given-up event can be redelivered."),
        };
    }

    private static async Task<Accepted<OutboxRedeliveryDto>> RedeliverAllGivenUpAsync(
        OutboxRedelivery redelivery, CancellationToken cancellationToken)
    {
        var count = await redelivery.RedeliverAllGivenUpAsync(cancellationToken);
        return TypedResults.Accepted((string?)null, new OutboxRedeliveryDto(count));
    }
}
