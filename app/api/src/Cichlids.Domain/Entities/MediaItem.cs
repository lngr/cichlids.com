using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A stored file (photo or video): the original upload plus enough metadata to serve and
/// re-derive renditions. Generated renditions are separate MediaVariant rows.
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

    /// <summary>
    /// The length of a video in milliseconds, or null for a photo.
    /// </summary>
    public int? DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Whether the camera of the app took the photo. Only such photos count as evidence of a guided
    /// water change and for the time-lapse streak.
    /// </summary>
    public bool CapturedInApp { get; set; }

    /// <summary>
    /// The moment the server stored the upload.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// The wall-clock time the camera recorded as the capture time (EXIF DateTimeOriginal), with
    /// DateTimeKind Unspecified, or null when the photo has no plausible capture time. It is not
    /// UTC: TakenAtOffsetMinutes says which offset applies, and is null when the camera recorded none.
    /// </summary>
    public DateTime? TakenAtLocal { get; set; }

    /// <summary>
    /// The offset from UTC in minutes that goes with TakenAtLocal (EXIF OffsetTimeOriginal), or null
    /// when the camera recorded none and the zone of TakenAtLocal is unknown.
    /// </summary>
    public short? TakenAtOffsetMinutes { get; set; }

    /// <summary>
    /// The moment the capture time of this item was last read from its original, or null when the
    /// original has not been examined. A set value with a null TakenAtLocal means the original has
    /// no usable capture time.
    /// </summary>
    public DateTimeOffset? CaptureCheckedAt { get; set; }

    /// <summary>
    /// The capture time as a DateTimeOffset: the wall-clock time with its recorded offset, or with
    /// offset zero when none was recorded (TakenAtOffsetMinutes is null then, and the value is
    /// not a UTC instant). Null when TakenAtLocal is null.
    /// </summary>
    public DateTimeOffset? TakenAt => TakenAtLocal is { } local
        ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromMinutes(TakenAtOffsetMinutes ?? 0))
        : null;

    public List<MediaVariant> Variants { get; set; } = [];

    /// <summary>
    /// The transcoding job of a video; null for a photo.
    /// </summary>
    public VideoJob? VideoJob { get; set; }
}
