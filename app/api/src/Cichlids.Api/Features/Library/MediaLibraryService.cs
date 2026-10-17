using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Library;

/// <summary>
/// Writes the media library of a member: stores a photo without links and deletes a file with its
/// renditions and all its links, and links photos of the member to a tank. Deleting a file is the only way a
/// file leaves the library. Problem codes: library_photo_invalid, upload_rate_limited, media_not_found,
/// media_already_linked and media_is_proof.
/// </summary>
public sealed class MediaLibraryService(
    CichlidsDbContext context, UploadsWriteService uploads, MediaLibraryQueryService query, StoredObjectDeleter deleter,
    VideoRemovalService videoRemoval, TimeProvider clock)
{
    public const string PhotoInvalidCode = "library_photo_invalid";
    public const string NotFoundCode = "media_not_found";
    public const string IsProofCode = "media_is_proof";
    public const string AlreadyLinkedCode = "media_already_linked";

    /// <summary>
    /// Stores the uploaded photo in the library of the member without a link. The photo lies in the private
    /// storage area until a link makes it visible to others. The error is a problem code.
    /// </summary>
    public async Task<(MediaLibraryItemDto? Item, string? Error)> AddPhotoAsync(long profileId, IFormFile? file, CancellationToken cancellationToken)
    {
        var (image, _) = await UploadedImageReader.ReadAsync(file, cancellationToken);
        if (image is null)
        {
            return (null, PhotoInvalidCode);
        }

        if (await uploads.CheckHourlyQuotaAsync(profileId, cancellationToken) is not null)
        {
            return (null, UploadsEndpoints.UploadRateLimitedCode);
        }

        StoredImage stored;
        try
        {
            stored = await uploads.StoreImageAsync(profileId, image.Bytes, image.Format, Path.GetFileName(file!.FileName), isPrivate: true,
                cancellationToken, privateArea: "library");
        }
        catch (Exception ex) when (ex is ImageProcessor.ImageTooLargeException or ImageProcessor.UnsupportedImageFormatException
            or ImageProcessor.BrokenImageException)
        {
            return (null, PhotoInvalidCode);
        }

        try
        {
            context.MediaItems.Add(stored.Item);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await deleter.DeleteAsync(stored.WrittenKeys);
            throw;
        }

        return (await query.GetItemAsync(profileId, stored.Item.Id, cancellationToken), null);
    }

    /// <summary>
    /// Adds links from photos of the member to a tank with the given role, in the given order after the links the
    /// role already has. The caller owns the transaction and saves the context. The photos are locked against a
    /// concurrent deletion. A file that is not a photo of the member answers like a missing file
    /// (media_not_found); a photo that already has a link with this role to the tank answers media_already_linked.
    /// Nothing is added when an error is returned.
    /// </summary>
    public async Task<string?> LinkPhotosToTankAsync(
        long profileId, long tankId, MediaLinkRole role, IReadOnlyList<long> mediaItemIds, CancellationToken cancellationToken)
    {
        if (mediaItemIds.Count == 0)
        {
            return null;
        }

        var ids = mediaItemIds.Distinct().ToArray();
        var locked = await context.MediaItems
            .FromSqlInterpolated($"SELECT * FROM public.media_item WHERE id = ANY({ids}) AND owner_profile_id = {profileId} AND kind = 'photo' ORDER BY id FOR UPDATE")
            .Select(m => m.Id).ToListAsync(cancellationToken);
        if (locked.Count != ids.Length)
        {
            return NotFoundCode;
        }

        var existing = await context.MediaLinks.AsNoTracking().Where(l => l.TankId == tankId && l.Role == role)
            .Select(l => new { l.MediaItemId, l.Position }).ToListAsync(cancellationToken);
        if (existing.Any(l => ids.Contains(l.MediaItemId)))
        {
            return AlreadyLinkedCode;
        }

        var next = existing.Count == 0 ? 0 : existing.Max(l => l.Position) + 1;
        foreach (var id in mediaItemIds)
        {
            context.MediaLinks.Add(MediaLink.ForTank(tankId, id, role, next++));
        }

        return null;
    }

    /// <summary>
    /// Deletes the file of the member with its renditions and all its links and returns the places it was
    /// linked to. A published story that loses its last item is removed with it, a draft story is deleted.
    /// The stored objects are deleted after the commit. A file that proves a decommissioning stays. A file
    /// of another member answers like a missing file (media_not_found).
    /// </summary>
    public async Task<(IReadOnlyList<MediaPlaceDto>? Places, string? Error)> DeleteAsync(
        long profileId, long mediaItemId, CancellationToken cancellationToken)
    {
        MediaItem item;
        IReadOnlyList<MediaPlaceDto> places;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            // The row lock serializes the deletion against a concurrent link to the file.
            var locked = await context.MediaItems
                .FromSqlInterpolated($"SELECT * FROM public.media_item WHERE id = {mediaItemId} AND owner_profile_id = {profileId} FOR UPDATE")
                .Select(m => m.Id).SingleOrDefaultAsync(cancellationToken);
            if (locked == 0)
            {
                return (null, NotFoundCode);
            }

            if (await context.TankDecommissionings.AnyAsync(d => d.StartMediaItemId == mediaItemId || d.EndMediaItemId == mediaItemId, cancellationToken))
            {
                return (null, IsProofCode);
            }

            item = await context.MediaItems.Include(m => m.Variants).SingleAsync(m => m.Id == mediaItemId, cancellationToken);
            places = (await query.PlacesOfAsync(profileId, [mediaItemId], cancellationToken)).GetValueOrDefault(mediaItemId) ?? [];
            var links = await context.MediaLinks.AsNoTracking().Where(l => l.MediaItemId == mediaItemId)
                .Select(l => new { l.CareEntryId, l.StoryId }).ToListAsync(cancellationToken);
            var careEntryIds = links.Select(l => l.CareEntryId).OfType<long>().Distinct().ToList();
            var storyIds = links.Select(l => l.StoryId).OfType<long>().Distinct().ToList();

            await context.MediaLinks.Where(l => l.MediaItemId == mediaItemId).ExecuteDeleteAsync(cancellationToken);
            await RemoveEmptyStoriesAsync(profileId, storyIds, cancellationToken);
            await RecordCareChangesAsync(careEntryIds, cancellationToken);
            await context.MediaItems.Where(m => m.Id == mediaItemId).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (item.Kind == MediaKind.Video)
        {
            await videoRemoval.DeleteObjectsAsync([item]);
        }
        else
        {
            await deleter.DeleteAsync(item.Variants.Select(v => v.StorageKey).Append(item.StorageKey));
        }

        return (places, null);
    }

    // A story without an item shows nothing: a draft that was never published is deleted, every other story is removed.
    private async Task RemoveEmptyStoriesAsync(long profileId, List<long> storyIds, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var storyId in storyIds)
        {
            if (await context.MediaLinks.AnyAsync(l => l.StoryId == storyId, cancellationToken))
            {
                continue;
            }

            var drafts = await context.Stories.Where(s => s.Id == storyId && s.State == StoryState.Draft && !context.SlugAliases.Any(a => a.StoryId == s.Id)).ExecuteDeleteAsync(cancellationToken);
            if (drafts == 0)
            {
                await context.Stories.Where(s => s.Id == storyId && s.DeletedAt == null).ExecuteUpdateAsync(
                    set => set.SetProperty(s => s.DeletedAt, now).SetProperty(s => s.DeleteReason, "media_deleted")
                        .SetProperty(s => s.DeletedByProfileId, profileId).SetProperty(s => s.UpdatedAt, now),
                    cancellationToken);
            }
        }
    }

    // Care entries that lost a photo or video record a care.updated event with their new state.
    private async Task RecordCareChangesAsync(List<long> careEntryIds, CancellationToken cancellationToken)
    {
        if (careEntryIds.Count == 0)
        {
            return;
        }

        var entries = await CareEntryMapper.WithDetails(context.CareEntries.AsNoTracking()).Where(e => careEntryIds.Contains(e.Id)).ToListAsync(cancellationToken);
        foreach (var entry in entries)
        {
            CareEvents.Add(context, "care.updated", entry, clock.GetUtcNow());
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
