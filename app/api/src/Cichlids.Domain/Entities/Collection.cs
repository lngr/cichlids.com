namespace Cichlids.Domain.Entities;

/// <summary>
/// A member-curated, ordered set of posts.
/// </summary>
public class Collection
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public long ProfileId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsPublic { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public List<CollectionEntry> Entries { get; set; } = [];
}
