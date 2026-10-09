using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A discussion thread: the platform's own entity for both migrated forum content and, once the
/// community section opens for writing, new discussions (ADR-0021). <see cref="LastPostAt"/> and
/// <see cref="PostCount"/> are denormalized and recomputed from the thread's posts.
/// </summary>
public class DiscussionThread
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public DiscussionCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public DiscussionThreadState State { get; set; } = DiscussionThreadState.Archived;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastPostAt { get; set; }
    public int PostCount { get; set; }

    public List<DiscussionPost> Posts { get; set; } = [];
}
