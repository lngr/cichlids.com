namespace Cichlids.Domain.Entities;

/// <summary>
/// A star rating on exactly one post or one tank, kept as its own row separate from
/// <see cref="Comment"/> so rating and commenting can evolve independently.
/// </summary>
public class Rating
{
    public long Id { get; set; }
    public int? LegacyCommentId { get; set; }
    public long? PostId { get; set; }
    public long? TankId { get; set; }
    public long? ProfileId { get; set; }
    public short Stars { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
