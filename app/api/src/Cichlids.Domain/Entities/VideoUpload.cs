using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A resumable video upload of a member. The file arrives in chunks of ChunkSize bytes, each
/// stored as one part of a multipart upload in the object store, so any API instance accepts the
/// next chunk. Offset is the number of bytes received so far; the upload is complete when
/// Offset equals ByteSize. An upload that is not completed before ExpiresAt is abandoned and
/// cleaned up.
/// </summary>
public class VideoUpload
{
    public long Id { get; set; }
    public long OwnerProfileId { get; set; }
    public VideoUploadTarget Target { get; set; }
    public long TankId { get; set; }

    /// <summary>
    /// The care entry the video is attached to; null for a tank video.
    /// </summary>
    public long? CareEntryId { get; set; }

    public string? OriginalFilename { get; set; }

    /// <summary>
    /// The size in bytes the client announced for the whole file.
    /// </summary>
    public long ByteSize { get; set; }

    public int ChunkSize { get; set; }
    public long Offset { get; set; }

    /// <summary>
    /// The key the original is stored under once the multipart upload is completed, below the
    /// private prefix.
    /// </summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>
    /// The container family recognized from the first chunk: mp4 or quick_time.
    /// </summary>
    public string? Container { get; set; }

    public string MultipartUploadId { get; set; } = string.Empty;

    /// <summary>
    /// The entity tags of the received parts, in part order.
    /// </summary>
    public List<string> PartETags { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
