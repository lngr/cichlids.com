using Cichlids.Api.Features.Media;
using Cichlids.Domain;
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
/// playlist and the poster for a video that anonymous viewers see and whose files lie in the public
/// area, signed paths for every other video. A video is ready when its job is done, failed when its job
/// failed and processing otherwise; a ready video that has no public URL and whose caller allows no
/// signed path is processing. The visibility rule decides the kind of URL: a video that anonymous viewers
/// do not see (anonymousVisible false) never gets a public URL, even while its files lie under the
/// public prefix.
/// </summary>
public sealed class VideoQueryService(CichlidsDbContext context, IObjectStore store, MediaUrlSigner? signer = null)
{
    /// <summary>
    /// Loads the media items of a video query together with their variants and jobs.
    /// </summary>
    public static IQueryable<MediaItem> WithDetails(IQueryable<MediaItem> items) =>
        items.Include(m => m.Variants).Include(m => m.VideoJob);

    /// <summary>
    /// The videos of a tank, newest first, or null when the tank is not available to the viewer.
    /// The owner sees every video including those still processing or failed; everybody else sees
    /// the ready videos of a published tank whose profile level allows them.
    /// </summary>
    public async Task<IReadOnlyList<VideoDto>?> ListForTankAsync(long tankId, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var tank = await context.Tanks.AsNoTracking().Where(t => t.Id == tankId && t.DeletedAt == null)
            .Select(t => new
            {
                t.ProfileId,
                t.State,
                t.ProfileVisibility,
                OwnerVisible = context.Profiles.Any(p => p.Id == t.ProfileId && (p.SuspendedUntil == null || p.SuspendedUntil <= now)),
            })
            .SingleOrDefaultAsync(cancellationToken);
        var isOwner = tank is not null && viewerProfileId == tank.ProfileId;
        var (isFollower, isSelected) = await context.RelationToTankAsync(tankId, viewerProfileId, cancellationToken);
        if (tank is null || (!isOwner && (tank.State != TankState.Published || !tank.OwnerVisible
                || !TankVisibilityRules.CanSeeProfile(
                    TankVisibilityRules.ViewerOf(viewerProfileId, tank.ProfileId, isFollower, isSelected), tank.ProfileVisibility))))
        {
            return null;
        }

        var ids = context.MediaLinks.Where(l => l.TankId == tankId && l.Role == MediaLinkRole.Video).Select(l => l.MediaItemId);
        var items = await WithDetails(context.MediaItems.AsNoTracking().Where(m => ids.Contains(m.Id) && m.Kind == MediaKind.Video))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).ToListAsync(cancellationToken);
        var itemIds = items.Select(i => i.Id).ToList();
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(itemIds, now, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(itemIds, viewerProfileId, now, cancellationToken);
        var videos = items.Select(i => ToPublicDto(i, store, signer, allowSigned: true, anonymousVisible.Contains(i.Id), generations.GetValueOrDefault(i.Id)));
        return (isOwner ? videos : videos.Where(v => v.State == VideoState.Ready)).ToList();
    }

    /// <summary>
    /// The representation of a video, with public URLs once it is ready and anonymous viewers see it, and
    /// with signed paths otherwise when the caller allows them.
    /// </summary>
    public static VideoDto ToPublicDto(
        MediaItem item, IObjectStore store, MediaUrlSigner? signer, bool allowSigned, bool anonymousVisible, long? issuableGeneration)
    {
        var state = StateOf(item);
        var hls = Variant(item, VideoStorage.HlsLabel);
        var poster = Variant(item, VideoStorage.PosterLabel);
        var ready = state == VideoState.Ready && hls is not null && poster is not null;
        if (!ready)
        {
            return Build(item, state, null, null);
        }

        if (!anonymousVisible || VideoStorage.IsPrivate(item))
        {
            return allowSigned && signer is not null && issuableGeneration is { } generation
                ? Build(item, state, signer.VideoPathFor(item.Id, generation, PosterFile), signer.VideoPathFor(item.Id, generation, MasterFile))
                : Build(item, VideoState.Processing, null, null);
        }

        return Build(item, state, PublicUrl(store, poster!.StorageKey), PublicUrl(store, hls!.StorageKey));
    }

    /// <summary>
    /// The representation of a video with signed paths once it is ready and a generation was read with the
    /// visibility check of the viewer; without one a ready video is processing.
    /// </summary>
    public static VideoDto ToSignedDto(MediaItem item, MediaUrlSigner signer, long? issuableGeneration)
    {
        var state = StateOf(item);
        var ready = state == VideoState.Ready && Variant(item, VideoStorage.HlsLabel) is not null;
        if (ready && issuableGeneration is not { } generation)
        {
            return Build(item, VideoState.Processing, null, null);
        }

        return Build(item, state, ready ? signer.VideoPathFor(item.Id, issuableGeneration!.Value, PosterFile) : null, ready ? signer.VideoPathFor(item.Id, issuableGeneration!.Value, MasterFile) : null);
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
