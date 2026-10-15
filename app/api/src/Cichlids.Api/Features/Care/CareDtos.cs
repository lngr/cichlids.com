using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Care;

public sealed record CareAmountRequest(decimal Value, string Unit);
public sealed record LogCareRequest(DateTimeOffset? OccurredAt = null, CareAmountRequest? Amount = null, string Kind = "water_change");
public sealed record UpdateCareRequest(CareAmountRequest? Amount);
public sealed record CareAmountDto(decimal Value, CareAmountUnit Unit, decimal? Liters);
public sealed record CareEntryDto(long Id, long TankId, long AuthorProfileId, CareKind Kind,
    DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, CareAmountDto? Amount)
{
    public static CareEntryDto From(CareEntry entry) => new(entry.Id, entry.TankId, entry.AuthorProfileId,
        entry.Kind, entry.OccurredAt, entry.RecordedAt, entry.AmountInputValue is { } value
            ? new(value, entry.AmountInputUnit!.Value, entry.AmountLiters) : null);
}
