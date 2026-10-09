namespace Cichlids.Domain.Entities;

/// <summary>
/// A generated rendition of a media item (for example a thumbnail or a transcoded video
/// rendition), identified within the item by its label.
/// </summary>
public class MediaVariant
{
    public long Id { get; set; }
    public long MediaItemId { get; set; }
    public string Label { get; set; } = string.Empty;
    public int? Width { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public long? ByteSize { get; set; }
}
