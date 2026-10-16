namespace Cichlids.Domain.Entities;

/// <summary>
/// One post within a discussion thread, ordered by Sort. A guest post has no AuthorProfileId and
/// keeps only its display name (ADR-0021); every other post is attributed to a profile and
/// carries no PosterName of its own. A check constraint in the database rejects a post with both.
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

    public List<MediaLink> MediaLinks { get; set; } = [];
}
