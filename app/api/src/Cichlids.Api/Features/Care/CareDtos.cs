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
public sealed record CarePhotoDto(long Id, long MediaItemId, int? Width, int? Height, CarePhotoUrlsDto Urls);
public sealed record CareEntryDto(long Id, long TankId, long AuthorProfileId, CareKind Kind,
    DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, CareAmountDto? Amount, IReadOnlyList<MeasurementValueDto>? Values,
    string? Note, IReadOnlyList<CarePhotoDto> Photos, IReadOnlyList<Videos.VideoDto> Videos, IReadOnlyList<CareTodoDto> Todos,
    int? DurationSeconds = null, long? WaterChangeSessionId = null, WaterChangeEvidenceDto? Evidence = null, bool OnlyMe = false);
public sealed record CareSeriesPointDto(long EntryId, DateTimeOffset OccurredAt, decimal Value, decimal InputValue,
    MeasurementUnit InputUnit, MeasurementOrigin? Origin);
public sealed record CareSeriesDto(CareParameter Parameter, MeasurementUnit Unit, IReadOnlyList<CareSeriesPointDto> Points);
public sealed record CareAllSeriesDto(IReadOnlyList<CareSeriesDto> Series);
public sealed record CareReasonDto(string Code, IReadOnlyDictionary<string, string>? Parameters);
public sealed record CareDueDto(CareDueState State, int Days, string? DueDate);
/// <summary>
/// A rule of a care plan. Paused tells whether a decommissioning of the tank pauses this kind of care; a paused rule has no due state.
/// </summary>
public sealed record CarePlanRuleDto(CareKind Kind, int? RhythmDays, bool Enabled, bool IsDefault, CareReasonDto? Reason, CareDueDto? Due, bool Paused = false);
public sealed record CarePlanDto(long TankId, IReadOnlyList<CarePlanRuleDto> Rules, bool RemindersEnabled);
public sealed record UpdateCarePlanRequest(bool? RemindersEnabled = null);
public sealed record UpdateCarePlanRuleRequest(int? RhythmDays = null, bool? Enabled = null);
/// <summary>
/// The water change streak of a due water change as the reminders need it. State is Running or Paused.
/// While the streak is running, ReminderFrom and Deadline are the first and the last local date on
/// which the member is reminded; while it is paused by a vacation, both are null and no reminder falls.
/// </summary>
public sealed record DueStreakDto(Cichlids.Domain.WaterChangeStreakState State, string? ReminderFrom, string? Deadline);

/// <summary>
/// A due rule of a tank. Streak is set for a water change whose streak is running or paused and is
/// null otherwise.
/// </summary>
public sealed record DueCareItemDto(long TankId, string TankTitle, CareKind Kind, int RhythmDays, CareReasonDto Reason, CareDueDto Due,
    bool RemindersEnabled, DueStreakDto? Streak = null);
public sealed record DueCareDto(IReadOnlyList<DueCareItemDto> Items);

/// <summary>
/// The care of one local calendar day: how many water changes and measurements were logged.
/// </summary>
public sealed record CareHeatmapDayDto(string Date, int WaterChanges, int Measurements);

/// <summary>
/// The care days of the last Weeks ISO weeks, from Start (a Monday) to End (today), in the local dates of
/// the reader. Days lists only days with care. LastWaterChange and LastMeasurement are the newest days of
/// their kind in the window, null when there is none. DecommissionedDays lists the days of the window on which
/// all care of the tank was paused by a decommissioning (on which every tank, for the own heatmap).
/// </summary>
public sealed record CareHeatmapDto(string Start, string End, int Weeks, IReadOnlyList<CareHeatmapDayDto> Days, int ActiveDays,
    string? LastWaterChange, string? LastMeasurement, IReadOnlyList<string> DecommissionedDays);
