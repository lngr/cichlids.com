namespace Cichlids.Domain.Entities;

/// <summary>
/// A public handle a member gave up, reserved for that member until ReservedUntil. Nobody else
/// takes the handle during that time; the member may take it back. Handle is stored lowercase and
/// is unique, so a handle has at most one reservation.
/// </summary>
public class ReleasedHandle
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public string Handle { get; set; } = string.Empty;
    public DateTimeOffset ReleasedAt { get; set; }
    public DateTimeOffset ReservedUntil { get; set; }
}
