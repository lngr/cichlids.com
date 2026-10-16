using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A follow relation from a profile to exactly one target: another profile or a tank. A follow of a tank
/// is Pending while the owner has not answered the request and Approved once the follower is accepted;
/// following a public tank is Approved at once. DecidedAt is the time of the approval.
/// </summary>
public class Follow
{
    public long Id { get; set; }
    public long FollowerProfileId { get; set; }
    public long? TargetProfileId { get; set; }
    public long? TargetTankId { get; set; }
    public FollowStatus Status { get; set; } = FollowStatus.Approved;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
