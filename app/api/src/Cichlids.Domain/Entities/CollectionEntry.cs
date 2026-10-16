namespace Cichlids.Domain.Entities;

/// <summary>
/// A story included in a collection, ordered within it.
/// </summary>
public class CollectionEntry
{
    public long Id { get; set; }
    public long CollectionId { get; set; }
    public long StoryId { get; set; }
    public int Sort { get; set; }
}
