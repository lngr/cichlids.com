namespace Cichlids.Domain.Entities;

/// <summary>
/// A media item (a migrated forum attachment) attached to a discussion post, ordered within it.
/// </summary>
public class DiscussionPostMedia
{
    public long Id { get; set; }
    public long DiscussionPostId { get; set; }
    public long MediaItemId { get; set; }
    public int Sort { get; set; }
}
