namespace Cichlids.Domain.Entities;

/// <summary>
/// A member's dismissal of a milestone on Today. Key is the stable key of the milestone (its kind, the
/// tank and the threshold or the tank year), so a milestone is dismissed once for ever.
/// </summary>
public class MilestoneDismissal
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset DismissedAt { get; set; }
}
