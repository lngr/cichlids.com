using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A gallery entry: an ordered sequence of items (media links with the item role, each with its own
/// caption) and subjects (tanks, inhabitants and species the story points to). The cover is one of
/// the items; a story without an explicit cover shows its first item. View count and rating
/// aggregate are denormalized and recomputed from the underlying interaction rows.
/// </summary>
public class Story
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public long AuthorProfileId { get; set; }

    /// <summary>
    /// The item link chosen as the cover, or null when the first item is the cover.
    /// </summary>
    public long? CoverMediaLinkId { get; set; }

    public StoryTopic Topic { get; set; } = StoryTopic.Unknown;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public StoryState State { get; set; } = StoryState.Draft;
    public long ViewCount { get; set; }
    public double? RatingAverage { get; set; }
    public int RatingCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the story last changed: its creation, a change of its content or media, or its
    /// publication. The age of an unpublished draft is measured from this moment.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeleteReason { get; set; }
    public long? DeletedByProfileId { get; set; }

    public List<MediaLink> MediaLinks { get; set; } = [];
    public List<StorySubject> Subjects { get; set; } = [];
}
