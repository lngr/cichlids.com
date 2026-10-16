using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A guided water change of a tank: the member checks off steps while it runs, optionally adds a
/// photo of the drained and of the refilled tank, and finishes it, which creates the water change
/// care entry. A session without a finish time is open.
/// </summary>
public class WaterChangeSession
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public long ProfileId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// Whole seconds from the start to the finish; null while the session is open.
    /// </summary>
    public int? DurationSeconds { get; set; }

    public long? DrainedMediaItemId { get; set; }
    public DateTimeOffset? DrainedPhotoAt { get; set; }

    /// <summary>
    /// Whether the client reported the drained photo as taken with the camera of the app; null
    /// without a photo.
    /// </summary>
    public bool? DrainedCapturedInApp { get; set; }

    public long? RefilledMediaItemId { get; set; }
    public DateTimeOffset? RefilledPhotoAt { get; set; }
    public bool? RefilledCapturedInApp { get; set; }

    public List<WaterChangeSessionStep> Steps { get; set; } = [];
}

/// <summary>
/// A step of a session that the member checked off or skipped, with the server time of the mark.
/// </summary>
public class WaterChangeSessionStep
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public WaterChangeStep Step { get; set; }
    public WaterChangeStepState State { get; set; }
    public DateTimeOffset At { get; set; }
}
