using Cichlids.Api.Features.Library;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Profiles;

/// <summary>
/// Sets, replaces and removes the avatar and the header of a profile. Both are profile links with at
/// most one per role. Only the owner of a file links it, and only a photo can serve as avatar or header.
/// Removing a link keeps the file in the media library. Every change moves the affected files into the
/// storage area that the visibility rule answers for. Problem codes: media_not_found,
/// profile_media_not_photo, profile_photo_invalid and upload_rate_limited.
/// </summary>
public sealed class ProfileMediaService(
    CichlidsDbContext context, UploadsWriteService uploads, StoredObjectDeleter deleter, MediaPlacementTrigger? placement = null)
{
    public const string NotPhotoCode = "profile_media_not_photo";
    public const string PhotoInvalidCode = "profile_photo_invalid";

    /// <summary>
    /// Links a photo the member owns as the avatar or header of the member and replaces the previous
    /// link of that role. The error is a problem code.
    /// </summary>
    public async Task<string?> SetAsync(long profileId, MediaLinkRole role, long mediaItemId, CancellationToken cancellationToken)
    {
        List<long> previous;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockProfileAsync(profileId, cancellationToken);
            // The row lock serializes the link against a concurrent deletion of the file from the library.
            var file = await context.MediaItems
                .FromSqlInterpolated($"SELECT * FROM public.media_item WHERE id = {mediaItemId} AND owner_profile_id = {profileId} FOR UPDATE")
                .Select(m => new { m.Kind }).SingleOrDefaultAsync(cancellationToken);
            if (file is null)
            {
                return MediaLibraryService.NotFoundCode;
            }

            if (file.Kind != MediaKind.Photo)
            {
                return NotPhotoCode;
            }

            previous = await context.MediaLinks.ProfileLinks(role).Where(l => l.ProfileId == profileId)
                .Select(l => l.MediaItemId).ToListAsync(cancellationToken);
            if (previous.Count != 1 || previous[0] != mediaItemId)
            {
                await context.MediaLinks.ProfileLinks(role).Where(l => l.ProfileId == profileId).ExecuteDeleteAsync(cancellationToken);
                context.MediaLinks.Add(MediaLink.ForProfile(profileId, mediaItemId, role));
                await context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await PlaceAsync([.. previous, mediaItemId]);
        return null;
    }

    /// <summary>
    /// Stores the uploaded photo in the media library of the member and links it as the avatar or header.
    /// The error is a problem code.
    /// </summary>
    public async Task<string?> SetFromPhotoAsync(long profileId, MediaLinkRole role, IFormFile? file, CancellationToken cancellationToken)
    {
        var (image, _) = await UploadedImageReader.ReadAsync(file, cancellationToken);
        if (image is null)
        {
            return PhotoInvalidCode;
        }

        if (await uploads.CheckHourlyQuotaAsync(profileId, cancellationToken) is not null)
        {
            return UploadsEndpoints.UploadRateLimitedCode;
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
            return PhotoInvalidCode;
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

        // The placement after the link moves the renditions by statements on the database, so the stored entities must not
        // answer later reads with their keys from before the move.
        context.ChangeTracker.Clear();
        return await SetAsync(profileId, role, stored.Item.Id, cancellationToken);
    }

    /// <summary>
    /// Removes the link of the role; the file stays in the media library. Without a link nothing changes.
    /// </summary>
    public async Task RemoveAsync(long profileId, MediaLinkRole role, CancellationToken cancellationToken)
    {
        List<long> affected;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockProfileAsync(profileId, cancellationToken);
            affected = await context.MediaLinks.ProfileLinks(role).Where(l => l.ProfileId == profileId)
                .Select(l => l.MediaItemId).ToListAsync(cancellationToken);
            await context.MediaLinks.ProfileLinks(role).Where(l => l.ProfileId == profileId).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await PlaceAsync(affected);
    }

    private Task LockProfileAsync(long profileId, CancellationToken cancellationToken) =>
        context.Profiles.FromSqlInterpolated($"SELECT * FROM public.profile WHERE id = {profileId} FOR UPDATE")
            .Select(p => p.Id).SingleAsync(cancellationToken);

    private async Task PlaceAsync(List<long> mediaItemIds)
    {
        if (placement is not null)
        {
            await placement.PlaceNowAsync([.. mediaItemIds.Distinct()]);
        }
    }
}
