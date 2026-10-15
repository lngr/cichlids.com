using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Care;

public sealed record CareAmountRequest(decimal Value, string Unit);
public sealed record MeasurementValueRequest(string Parameter, decimal Value, string? Unit = null, string? Origin = null);
public sealed record LogCareRequest(DateTimeOffset? OccurredAt = null, CareAmountRequest? Amount = null, string Kind = "water_change",
    IReadOnlyList<MeasurementValueRequest>? Values = null);
public sealed record UpdateCareRequest(CareAmountRequest? Amount, IReadOnlyList<MeasurementValueRequest>? Values = null);
public sealed record CareAmountDto(decimal Value, CareAmountUnit Unit, decimal? Liters);
public sealed record MeasurementValueDto(CareParameter Parameter, decimal Value, MeasurementUnit Unit,
    decimal NormalizedValue, MeasurementUnit CanonicalUnit, MeasurementOrigin? Origin)
{
    public static MeasurementValueDto From(CareMeasurementValue value) => new(value.Parameter, value.InputValue, value.InputUnit,
        value.NormalizedValue, CareMeasurements.RuleFor(value.Parameter).CanonicalUnit, value.Origin);
}
public sealed record CareEntryDto(long Id, long TankId, long AuthorProfileId, CareKind Kind,
    DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, CareAmountDto? Amount, IReadOnlyList<MeasurementValueDto>? Values = null)
{
    public static CareEntryDto From(CareEntry entry) => new(entry.Id, entry.TankId, entry.AuthorProfileId,
        entry.Kind, entry.OccurredAt, entry.RecordedAt, entry.AmountInputValue is { } value
            ? new(value, entry.AmountInputUnit!.Value, entry.AmountLiters) : null,
        entry.Kind == CareKind.Measurement
            ? entry.Values.OrderBy(v => v.Parameter).Select(MeasurementValueDto.From).ToList() : null);
}
public sealed record CareSeriesPointDto(long EntryId, DateTimeOffset OccurredAt, decimal Value, decimal InputValue,
    MeasurementUnit InputUnit, MeasurementOrigin? Origin);
public sealed record CareSeriesDto(CareParameter Parameter, MeasurementUnit Unit, IReadOnlyList<CareSeriesPointDto> Points);
