using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Care;

public sealed record CareAmountRequest(decimal Value, string Unit);
public sealed record MeasurementValueRequest(string Parameter, decimal Value, string? Unit = null, string? Origin = null);
public sealed record LogCareRequest(DateTimeOffset? OccurredAt = null, CareAmountRequest? Amount = null, string Kind = "water_change",
    IReadOnlyList<MeasurementValueRequest>? Values = null, bool OnlyMe = false);
/// <summary>
/// The change of a care entry. A request that carries a note and neither an amount nor values changes only the note; an
/// empty or blank note removes it. A request that carries an occurred time and neither an amount nor values changes only the time (and
/// the note when it has one). Any other request replaces the amount or the values of the entry, and a note or an occurred time in it
/// applies as well. OnlyMe changes whether only the owner sees the entry and can be sent alone.
/// </summary>
public sealed record UpdateCareRequest(CareAmountRequest? Amount, IReadOnlyList<MeasurementValueRequest>? Values = null, string? Note = null,
    DateTimeOffset? OccurredAt = null, bool? OnlyMe = null);
public sealed record CareAmountDto(decimal Value, CareAmountUnit Unit, decimal? Liters);
public sealed record MeasurementValueDto(CareParameter Parameter, decimal Value, MeasurementUnit Unit,
    decimal NormalizedValue, MeasurementUnit CanonicalUnit, MeasurementOrigin? Origin)
{
    public static MeasurementValueDto From(CareMeasurementValue value) => new(value.Parameter, value.InputValue, value.InputUnit,
        value.NormalizedValue, CareMeasurements.RuleFor(value.Parameter).CanonicalUnit, value.Origin);
}
public sealed record CarePhotoUrlsDto(string? Thumb, string? Small, string? Medium, string? Large);
public sealed record CarePhotoDto(long Id, int? Width, int? Height, CarePhotoUrlsDto Urls);
public sealed record CareEntryDto(long Id, long TankId, long AuthorProfileId, CareKind Kind,
    DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, CareAmountDto? Amount, IReadOnlyList<MeasurementValueDto>? Values,
    string? Note, IReadOnlyList<CarePhotoDto> Photos, IReadOnlyList<Videos.VideoDto> Videos, IReadOnlyList<CareTodoDto> Todos,
    int? DurationSeconds = null, long? WaterChangeSessionId = null, WaterChangeEvidenceDto? Evidence = null, bool OnlyMe = false);
public sealed record CareSeriesPointDto(long EntryId, DateTimeOffset OccurredAt, decimal Value, decimal InputValue,
    MeasurementUnit InputUnit, MeasurementOrigin? Origin);
public sealed record CareSeriesDto(CareParameter Parameter, MeasurementUnit Unit, IReadOnlyList<CareSeriesPointDto> Points);
public sealed record CareReasonDto(string Code, IReadOnlyDictionary<string, string>? Parameters);
public sealed record CareDueDto(CareDueState State, int Days, string? DueDate);
public sealed record CarePlanRuleDto(CareKind Kind, int? RhythmDays, bool Enabled, bool IsDefault, CareReasonDto? Reason, CareDueDto? Due);
public sealed record CarePlanDto(long TankId, IReadOnlyList<CarePlanRuleDto> Rules, bool RemindersEnabled);
public sealed record UpdateCarePlanRequest(bool? RemindersEnabled = null);
public sealed record UpdateCarePlanRuleRequest(int? RhythmDays = null, bool? Enabled = null);
public sealed record DueCareItemDto(long TankId, string TankTitle, CareKind Kind, int RhythmDays, CareReasonDto Reason, CareDueDto Due,
    bool RemindersEnabled);
public sealed record DueCareDto(IReadOnlyList<DueCareItemDto> Items);
