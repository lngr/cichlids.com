namespace Cichlids.Domain.Entities;

/// <summary>
/// A follow relation from a profile to exactly one target: another profile or a tank.
/// </summary>
public class Follow
{
    public long Id { get; set; }
    public long FollowerProfileId { get; set; }
    public long? TargetProfileId { get; set; }
    public long? TargetTankId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
