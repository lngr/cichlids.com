using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Records the outbox events of care entries. An event holds the state of the entry after the
/// change, or its last state for a deleted entry.
/// </summary>
public static class CareEvents
{
    public static void Add(CichlidsDbContext context, string type, CareEntry entry, DateTimeOffset now)
    {
        var payload = new Dictionary<string, object?>
        {
            ["careEntryId"] = entry.Id, ["tankId"] = entry.TankId, ["authorProfileId"] = entry.AuthorProfileId,
            ["kind"] = SnakeCaseEnum.ToSnakeCase(entry.Kind), ["occurredAt"] = entry.OccurredAt, ["recordedAt"] = entry.RecordedAt,
            ["note"] = entry.Note, ["onlyMe"] = entry.OnlyMe,
            ["amount"] = entry.AmountInputValue is { } value ? new { value, unit = SnakeCaseEnum.ToSnakeCase(entry.AmountInputUnit!.Value), liters = entry.AmountLiters } : null,
            ["photos"] = entry.Photos.OrderBy(p => p.Position).Select(p => new { id = p.Id, mediaItemId = p.MediaItemId, position = p.Position }).ToList(),
        };
        if (entry.WaterChangeSessionId is { } sessionId)
        {
            payload["waterChangeSessionId"] = sessionId;
            payload["durationSeconds"] = entry.DurationSeconds;
            payload["evidence"] = new { state = SnakeCaseEnum.ToSnakeCase(entry.Evidence!.Value), missing = entry.EvidenceMissing };
        }
        if (entry.Kind == CareKind.Measurement)
            payload["values"] = entry.Values.OrderBy(v => v.Parameter).Select(v => new
            {
                parameter = SnakeCaseEnum.ToSnakeCase(v.Parameter), value = v.InputValue, unit = SnakeCaseEnum.ToSnakeCase(v.InputUnit),
                normalizedValue = v.NormalizedValue, canonicalUnit = SnakeCaseEnum.ToSnakeCase(CareMeasurements.RuleFor(v.Parameter).CanonicalUnit),
                origin = v.Origin is { } origin ? SnakeCaseEnum.ToSnakeCase(origin) : null,
            }).ToList();
        context.OutboxEvents.Add(OutboxEvent.Create(type, "care", entry.Id.ToString(), JsonSerializer.Serialize(payload), now));
    }
}
