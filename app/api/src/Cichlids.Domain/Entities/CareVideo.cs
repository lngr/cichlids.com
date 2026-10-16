namespace Cichlids.Domain.Entities;

/// <summary>
/// A video attached to a care entry: the media item that stores it. An entry holds at most two
/// videos.
/// </summary>
public class CareVideo
{
    public const int MaxPerEntry = 2;

    public long Id { get; set; }
    public long CareEntryId { get; set; }
    public long MediaItemId { get; set; }
    public MediaItem MediaItem { get; set; } = null!;
}
