namespace Cichlids.Domain.Entities;

/// <summary>
/// A photo attached to a care entry: the media item that stores it and its position among the
/// photos of the entry. An entry holds at most six photos, at positions 0 to 5.
/// </summary>
public class CarePhoto
{
    public const int MaxPerEntry = 6;

    public long Id { get; set; }
    public long CareEntryId { get; set; }
    public long MediaItemId { get; set; }
    public int Position { get; set; }
    public MediaItem MediaItem { get; set; } = null!;
}
