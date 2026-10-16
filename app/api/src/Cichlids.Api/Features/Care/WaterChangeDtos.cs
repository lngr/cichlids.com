using Cichlids.Domain;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Care;

public sealed record WaterChangeStepDto(WaterChangeStep Step, WaterChangeStepState State, DateTimeOffset At);

/// <summary>
/// A photo of a guided water change: its private URLs, whether the client reported the app camera
/// as its source, when the server received it and the capture time from its EXIF data when present.
/// </summary>
public sealed record WaterChangePhotoDto(CarePhotoUrlsDto Urls, int? Width, int? Height, bool CapturedInApp,
    DateTimeOffset ReceivedAt, DateTimeOffset? TakenAt);

public sealed record WaterChangeEvidenceDto(WaterChangeEvidenceState State, IReadOnlyList<string> Missing)
{
    public static WaterChangeEvidenceDto From(WaterChangeEvidence evidence) => new(evidence.State, evidence.Missing);
}

/// <summary>
/// A guided water change session. PreviousDrainedPhoto holds the URLs of the drained photo of the
/// last finished session of the tank, for the overlay of the camera. MinDurationSeconds is the
/// duration that counts as verified.
/// </summary>
public sealed record WaterChangeSessionDto(long Id, long TankId, DateTimeOffset StartedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? FinishedAt, int? DurationSeconds, IReadOnlyList<WaterChangeStepDto> Steps,
    WaterChangePhotoDto? DrainedPhoto, WaterChangePhotoDto? RefilledPhoto, CarePhotoUrlsDto? PreviousDrainedPhoto, int MinDurationSeconds);

public sealed record WaterChangeFinishedDto(WaterChangeSessionDto Session, CareEntryDto Entry);

public sealed record WaterChangeStepRequest(string State);

public sealed record FinishWaterChangeRequest(CareAmountRequest? Amount = null);

/// <summary>
/// The outcome of starting a session: the session and whether it is new (false when an open session
/// of the tank is returned).
/// </summary>
public sealed record WaterChangeStart(WaterChangeSessionDto Session, bool Created);
