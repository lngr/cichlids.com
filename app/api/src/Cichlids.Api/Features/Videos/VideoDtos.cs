namespace Cichlids.Api.Features.Videos;

/// <summary>
/// Where the processing of a video stands for its owner: being transcoded, playable or failed.
/// </summary>
public enum VideoState
{
    Processing,
    Ready,
    Failed,
}

/// <summary>
/// A video of a tank or a care entry. The playlist and poster URLs are set once the video is
/// ready; ErrorCode is the problem code of a failed video. The size is that of the largest
/// rendition.
/// </summary>
public sealed record VideoDto(
    long Id, VideoState State, double? DurationSeconds, int? Width, int? Height, string? PosterUrl, string? PlaylistUrl,
    DateTimeOffset CreatedAt, string? ErrorCode);

/// <summary>
/// An upload session: its id, the bytes received so far (the offset the next chunk starts at),
/// the announced size, the chunk size and when the session is abandoned.
/// </summary>
public sealed record VideoUploadDto(long Id, long Offset, long Size, int ChunkSize, DateTimeOffset ExpiresAt);

/// <summary>
/// Body of a request that opens an upload session. The target is tank or care_entry; a care
/// entry target also names the care entry.
/// </summary>
public sealed record CreateVideoUploadRequest(string? Target, long? TankId, long? CareEntryId, long? Size, string? FileName);
