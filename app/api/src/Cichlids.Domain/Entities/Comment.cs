namespace Cichlids.Domain.Entities;

/// <summary>
/// A text comment on exactly one post or one tank. The author is usually a profile; migrated
/// comments without a resolvable author keep only the legacy display name.
/// </summary>
public class Comment
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public long? PostId { get; set; }
    public long? TankId { get; set; }
    public long? AuthorProfileId { get; set; }
    public string? PosterName { get; set; }
    public string Body { get; set; } = string.Empty;
    public int Score { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeleteReason { get; set; }
    public long? DeletedByProfileId { get; set; }

    public List<CommentVote> Votes { get; set; } = [];
}
