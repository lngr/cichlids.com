using Cichlids.Domain;

namespace Cichlids.Api.Features.Streaks;

/// <summary>
/// The water change streak of a tank. Deadline, FreezesLeft and FreezesPerYear belong to the owner and
/// are null for other viewers, who also never see the state Paused. FreezesPerYear is the allotment of freezes per tank year.
/// </summary>
public sealed record WaterChangeStreakDto(WaterChangeStreakState State, int Current, int CurrentVerified, int Longest,
    string? LastWaterChange, string? Deadline, int? FreezesLeft, int? FreezesPerYear);

/// <summary>
/// The time-lapse streak of a tank over ISO weeks. Paused and KeepingPhotoIds belong to the owner and
/// are null for other viewers.
/// </summary>
public sealed record TimeLapseStreakDto(int Current, int Longest, bool ThisWeekHasPhoto, bool ThisWeekOpen, string WeekStart,
    string WeekEnd, bool? Paused, IReadOnlyList<long>? KeepingPhotoIds);

/// <summary>
/// A vacation in the local dates of the member; EndDate is null while the vacation is active.
/// </summary>
public sealed record VacationDto(long Id, string StartDate, string? EndDate, bool Active);

/// <summary>
/// The streaks of one tank a viewer may see: the water change streak needs the care stream and is null
/// without it, the time-lapse streak needs the tank profile. Vacations are the ones of the owner of the
/// tank; other viewers see only the ended ones. A tank of a member with the streaks switched off has
/// no streaks and no vacations.
/// </summary>
public sealed record TankStreaksDto(long TankId, WaterChangeStreakDto? WaterChange, TimeLapseStreakDto? TimeLapse,
    IReadOnlyList<VacationDto> Vacations);

public sealed record OwnTankStreaksDto(long TankId, string Title, WaterChangeStreakDto WaterChange, TimeLapseStreakDto TimeLapse);

/// <summary>
/// The streaks of every live tank of the member with the vacations of the member. With Enabled false the
/// member has switched the streaks off and Vacations and Tanks are empty.
/// </summary>
public sealed record MeStreaksDto(bool Enabled, int FreezesPerYear, IReadOnlyList<VacationDto> Vacations, IReadOnlyList<OwnTankStreaksDto> Tanks);

public sealed record VacationListDto(IReadOnlyList<VacationDto> Items);

/// <summary>
/// Starts a vacation. Without an end date the vacation is active from the start date, which defaults to
/// the local date of today. With an end date the vacation is recorded afterwards and needs a start date.
/// </summary>
public sealed record StartVacationRequest(string? StartDate = null, string? EndDate = null);

/// <summary>
/// The days a member spent on ended vacations within a window of days ending today, and how many ended
/// vacations the member has. Fraction is VacationDays over WindowDays.
/// </summary>
public sealed record VacationShareDto(int VacationDays, int WindowDays, double Fraction, int PastVacationCount);
