using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A stored file (photo or video): the original upload plus enough metadata to serve and
/// re-derive renditions. Generated renditions are separate <see cref="MediaVariant"/> rows.
/// </summary>
public class MediaItem
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public long? OwnerProfileId { get; set; }
    public MediaKind Kind { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string? OriginalFilename { get; set; }
    public string? ContentType { get; set; }
    public long? ByteSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? ChecksumSha256 { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<MediaVariant> Variants { get; set; } = [];
}
