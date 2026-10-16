using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A publication unit: a single media item or a multi-media story, optionally linked to the
/// tank it was taken in. View count and rating aggregate are denormalized and recomputed from
/// the underlying interaction rows.
/// </summary>
public class Post
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public long AuthorProfileId { get; set; }
    public long? TankId { get; set; }
    public PostKind Kind { get; set; }
    public PostTopic Topic { get; set; } = PostTopic.Unknown;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public PostState State { get; set; } = PostState.Draft;
    public long ViewCount { get; set; }
    public double? RatingAverage { get; set; }
    public int RatingCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the post last changed: its creation, a change of its content or media, or its
    /// publication. The age of an unpublished draft is measured from this moment.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeleteReason { get; set; }
    public long? DeletedByProfileId { get; set; }

    public List<MediaLink> MediaLinks { get; set; } = [];
    public List<PostSpecies> Species { get; set; } = [];
}
