namespace Cichlids.Domain.Entities;

/// <summary>
/// An up- or down-vote by a profile on a comment; at most one vote per profile and comment.
/// </summary>
public class CommentVote
{
    public long Id { get; set; }
    public long CommentId { get; set; }
    public long ProfileId { get; set; }
    public short Value { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
