namespace Cichlids.Domain.Entities;

/// <summary>
/// A member the owner of a tank named explicitly, for example family or friends. A selected member
/// reaches the levels selected and followers of the tank without following it.
/// </summary>
public class TankSelectedMember
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public long MemberProfileId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
