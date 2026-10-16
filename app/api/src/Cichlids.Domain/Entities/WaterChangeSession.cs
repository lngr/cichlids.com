using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A guided water change of a tank: the member checks off steps while it runs, adds up to
/// MaxPhotos photos and finishes it, which creates the water change care entry. A session without
/// a finish time is open.
/// </summary>
public class WaterChangeSession
{
    public const int MaxPhotos = 6;

    public long Id { get; set; }
    public long TankId { get; set; }
    public long ProfileId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// Whole seconds from the start to the finish; null while the session is open.
    /// </summary>
    public int? DurationSeconds { get; set; }

    public List<WaterChangeSessionPhoto> Photos { get; set; } = [];

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

/// <summary>
/// A photo of a session: its position in the order of taking, whether the client reported the
/// camera of the app as its source and the server time at which the photo was received.
/// </summary>
public class WaterChangeSessionPhoto
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public long MediaItemId { get; set; }
    public int Position { get; set; }
    public bool CapturedInApp { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
