namespace Cichlids.Domain.Entities;

/// <summary>
/// One post within a discussion thread, ordered by <see cref="Sort"/>. A guest post has no
/// <see cref="AuthorProfileId"/> and keeps only its display name (ADR-0021); every other post is
/// attributed to a profile and carries no <see cref="PosterName"/> of its own.
/// </summary>
public class DiscussionPost
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public long ThreadId { get; set; }
    public long? AuthorProfileId { get; set; }
    public string? PosterName { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public int Sort { get; set; }

    public List<DiscussionPostMedia> Media { get; set; } = [];
}
