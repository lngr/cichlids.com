using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// Removes the stored objects of videos, and removes the link of a video from a tank or a care entry
/// of its owner. The video stays in the media library of its owner. Deleting the stored objects of a
/// video follows the commit of its rows, so a failing delete leaves unreferenced objects behind
/// (logged) and never rows that point at missing objects.
/// </summary>
public sealed class VideoRemovalService(
    CichlidsDbContext context, IObjectStore store, StoredObjectDeleter deleter, CareEntryMapper mapper, TimeProvider clock,
    MediaPlacementTrigger? placement = null)
{
    /// <summary>
    /// Deletes the original and all HLS files of the given video media items. Items must be loaded
    /// with their variants.
    /// </summary>
    public async Task DeleteObjectsAsync(IEnumerable<MediaItem> videos)
    {
        var keys = new List<string>();
        foreach (var video in videos)
        {
            keys.AddRange(await VideoStorage.KeysOfAsync(store, video, CancellationToken.None));
        }

        await deleter.DeleteAsync(keys);
    }

    /// <summary>
    /// Removes the link of a video from the owner's tank. Returns false when the tank or the video is
    /// not the owner's.
    /// </summary>
    public async Task<bool> RemoveFromTankAsync(long tankId, long mediaItemId, long profileId, CancellationToken cancellationToken)
    {
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, cancellationToken))
            {
                return false;
            }

            var link = await context.MediaLinks.SingleOrDefaultAsync(
                l => l.TankId == tankId && l.MediaItemId == mediaItemId && l.Role == MediaLinkRole.Video
                    && context.MediaItems.Any(m => m.Id == mediaItemId && m.Kind == MediaKind.Video && m.OwnerProfileId == profileId),
                cancellationToken);
            if (link is null)
            {
                return false;
            }

            context.MediaLinks.Remove(link);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (placement is not null) await placement.PlaceNowAsync([mediaItemId]);
        return true;
    }

    /// <summary>
    /// Removes the link of a video from the owner's care entry and returns the entry, or null when the
    /// entry or the video is not the owner's.
    /// </summary>
    public async Task<CareEntryDto?> RemoveFromCareEntryAsync(
        long tankId, long entryId, long mediaItemId, long profileId, CancellationToken cancellationToken)
    {
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, cancellationToken))
            {
                return null;
            }

            var entry = await CareEntryMapper.WithDetails(context.CareEntries)
                .SingleOrDefaultAsync(e => e.Id == entryId && e.TankId == tankId && e.AuthorProfileId == profileId, cancellationToken);
            var video = entry?.MediaLinks.SingleOrDefault(l => l.Role == MediaLinkRole.Video && l.MediaItemId == mediaItemId);
            if (entry is null || video is null)
            {
                return null;
            }

            entry.MediaLinks.Remove(video);
            context.MediaLinks.Remove(video);
            CareEvents.Add(context, "care.updated", entry, clock.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (placement is not null) await placement.PlaceNowAsync([mediaItemId]);
        var remaining = await CareEntryMapper.WithDetails(context.CareEntries.AsNoTracking()).SingleAsync(e => e.Id == entryId, cancellationToken);
        await mapper.PrepareAsync([remaining], profileId, cancellationToken);
        return mapper.ToDto(remaining);
    }
}
