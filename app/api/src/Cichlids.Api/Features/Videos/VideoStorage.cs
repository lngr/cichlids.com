using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// The stored objects of a video: the private original and every file below its HLS directory.
/// </summary>
public static class VideoStorage
{
    public const string HlsLabel = "hls";
    public const string PosterLabel = "poster";

    /// <summary>
    /// The keys of the original and of all HLS files of a video media item, as stored in the
    /// object store.
    /// </summary>
    public static async Task<List<string>> KeysOfAsync(IObjectStore store, MediaItem item, bool isPrivate, CancellationToken cancellationToken)
    {
        var keys = new List<string> { item.StorageKey };
        await foreach (var key in store.ListKeysAsync(MediaStorageKeys.BuildVideoDirectory(item.StorageKey, isPrivate), cancellationToken))
        {
            keys.Add(key);
        }

        return keys;
    }

    /// <summary>
    /// Whether the HLS files of the media item are stored under the private prefix.
    /// </summary>
    public static bool IsPrivate(MediaItem item) =>
        item.Variants.Any(v => v.StorageKey.StartsWith("private/", StringComparison.Ordinal));
}
