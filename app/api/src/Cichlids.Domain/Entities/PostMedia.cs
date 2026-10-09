namespace Cichlids.Domain.Entities;

/// <summary>
/// A media item attached to a post, ordered within the post.
/// </summary>
public class PostMedia
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public long MediaItemId { get; set; }
    public int Sort { get; set; }
}
