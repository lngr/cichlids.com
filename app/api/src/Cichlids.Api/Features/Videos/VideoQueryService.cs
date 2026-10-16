using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Cichlids.Infrastructure.Video;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// Reads the videos of a tank and builds the representation of a video: public URLs of the HLS
/// playlist and the poster for a tank video, signed paths for the video of a care entry. A video
/// is ready when its job is done, failed when its job failed and processing otherwise.
/// </summary>
public sealed class VideoQueryService(CichlidsDbContext context, IObjectStore store)
{
    /// <summary>
    /// Loads the media items of a video query together with their variants and jobs.
    /// </summary>
    public static IQueryable<MediaItem> WithDetails(IQueryable<MediaItem> items) =>
        items.Include(m => m.Variants).Include(m => m.VideoJob);

    /// <summary>
    /// The videos of a tank, newest first, or null when the tank is not available to the viewer.
    /// The owner sees every video including those still processing or failed; everybody else sees
    /// the ready videos of a published tank.
    /// </summary>
    public async Task<IReadOnlyList<VideoDto>?> ListForTankAsync(long tankId, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var tank = await context.Tanks.AsNoTracking().Where(t => t.Id == tankId && t.DeletedAt == null)
            .Select(t => new
            {
                t.ProfileId,
                t.State,
                OwnerVisible = context.Profiles.Any(p => p.Id == t.ProfileId && (p.SuspendedUntil == null || p.SuspendedUntil <= now)),
            })
            .SingleOrDefaultAsync(cancellationToken);
        var isOwner = tank is not null && viewerProfileId == tank.ProfileId;
        if (tank is null || (!isOwner && (tank.State != TankState.Published || !tank.OwnerVisible)))
        {
            return null;
        }

        var ids = context.TankMedia.Where(m => m.TankId == tankId && m.Section == TankMediaSection.Video).Select(m => m.MediaItemId);
        var items = await WithDetails(context.MediaItems.AsNoTracking().Where(m => ids.Contains(m.Id) && m.Kind == MediaKind.Video))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).ToListAsync(cancellationToken);
        var videos = items.Select(i => ToPublicDto(i, store));
        return (isOwner ? videos : videos.Where(v => v.State == VideoState.Ready)).ToList();
    }

    /// <summary>
    /// The representation of a tank video, with public URLs once it is ready.
    /// </summary>
    public static VideoDto ToPublicDto(MediaItem item, IObjectStore store)
    {
        var state = StateOf(item);
        var hls = Variant(item, VideoStorage.HlsLabel);
        var poster = Variant(item, VideoStorage.PosterLabel);
        var ready = state == VideoState.Ready && hls is not null && poster is not null;
        return Build(item, state, ready ? PublicUrl(store, poster!.StorageKey) : null, ready ? PublicUrl(store, hls!.StorageKey) : null);
    }

    /// <summary>
    /// The representation of a care video, with signed paths once it is ready.
    /// </summary>
    public static VideoDto ToCareDto(MediaItem item, CareVideoUrlSigner signer)
    {
        var state = StateOf(item);
        var ready = state == VideoState.Ready && Variant(item, VideoStorage.HlsLabel) is not null;
        return Build(item, state, ready ? signer.PathFor(item.Id, PosterFile) : null, ready ? signer.PathFor(item.Id, MasterFile) : null);
    }

    private const string MasterFile = HlsTranscoder.MasterPlaylistName;
    private const string PosterFile = HlsTranscoder.PosterName;

    private static VideoDto Build(MediaItem item, VideoState state, string? posterUrl, string? playlistUrl) => new(
        item.Id, state, item.DurationMs is { } ms ? ms / 1000.0 : null, item.Width, item.Height, posterUrl, playlistUrl, item.CreatedAt,
        state == VideoState.Failed ? item.VideoJob?.ErrorCode : null);

    private static MediaVariant? Variant(MediaItem item, string label) => item.Variants.SingleOrDefault(v => v.Label == label);

    private static VideoState StateOf(MediaItem item) => item.VideoJob?.Status switch
    {
        VideoJobStatus.Done when Variant(item, VideoStorage.HlsLabel) is not null => VideoState.Ready,
        VideoJobStatus.Failed => VideoState.Failed,
        _ => VideoState.Processing,
    };

    private static string PublicUrl(IObjectStore store, string storageKey)
    {
        if (!MediaStorageKeys.PublicPrefixes.Any(prefix => storageKey.StartsWith(prefix, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Storage key '{storageKey}' is outside the public prefixes and has no public URL.");
        }

        return store.GetPublicUrl(storageKey);
    }
}
