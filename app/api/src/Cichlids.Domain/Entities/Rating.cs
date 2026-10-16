namespace Cichlids.Domain.Entities;

/// <summary>
/// A star rating on exactly one story or one tank, kept as its own row separate from
/// Comment so rating and commenting can evolve independently.
/// </summary>
public class Rating
{
    public long Id { get; set; }
    public int? LegacyCommentId { get; set; }
    public long? StoryId { get; set; }
    public long? TankId { get; set; }
    public long? ProfileId { get; set; }
    public short Stars { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
