using Cichlids.Api.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Adds photos to and removes photos from care entries. A photo goes through the upload pipeline
/// (same formats, limits and variants) and is stored under the private prefix. Each change and its
/// care.updated event commit in one transaction; the stored objects of a removed photo are deleted
/// after the commit.
/// </summary>
public sealed class CarePhotoService(
    CichlidsDbContext context, UploadsWriteService uploads, StoredObjectDeleter deleter, CareEntryMapper mapper, TimeProvider clock)
{
    /// <summary>
    /// Attaches the uploaded file as a new photo of the author's entry. The error is a problem code:
    /// care_not_found, care_photo_invalid, care_photo_limit_reached or upload_rate_limited.
    /// </summary>
    public async Task<(CareEntryDto? Entry, string? Error)> AddAsync(
        long tankId, long entryId, long profileId, IFormFile? file, CancellationToken ct)
    {
        var photoCount = await OwnPhotoCountAsync(tankId, entryId, profileId, ct);
        if (photoCount is null) return (null, "care_not_found");
        if (photoCount >= CarePhoto.MaxPerEntry) return (null, "care_photo_limit_reached");
        var (image, _) = await UploadedImageReader.ReadAsync(file, ct);
        if (image is null) return (null, "care_photo_invalid");
        if (await uploads.CheckHourlyQuotaAsync(profileId, ct) is not null) return (null, "upload_rate_limited");

        StoredImage stored;
        try
        {
            stored = await uploads.StoreImageAsync(profileId, image.Bytes, image.Format, Path.GetFileName(file!.FileName), isPrivate: true, ct);
        }
        catch (Exception ex) when (ex is ImageProcessor.ImageTooLargeException or ImageProcessor.UnsupportedImageFormatException
            or ImageProcessor.BrokenImageException)
        {
            return (null, "care_photo_invalid");
        }

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            string? refusal = null;
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) refusal = "care_not_found";
            var entry = refusal is null
                ? await context.CareEntries.Include(e => e.Values).Include(e => e.Photos)
                    .SingleOrDefaultAsync(e => e.Id == entryId && e.TankId == tankId && e.AuthorProfileId == profileId, ct)
                : null;
            refusal ??= entry is null ? "care_not_found" : entry.Photos.Count >= CarePhoto.MaxPerEntry ? "care_photo_limit_reached" : null;
            if (refusal is not null)
            {
                await deleter.DeleteAsync(stored.WrittenKeys);
                return (null, refusal);
            }

            var position = Enumerable.Range(0, CarePhoto.MaxPerEntry).First(slot => entry!.Photos.All(p => p.Position != slot));
            entry!.Photos.Add(new CarePhoto { MediaItem = stored.Item, Position = position });
            await context.SaveChangesAsync(ct);
            CareEvents.Add(context, "care.updated", entry, clock.GetUtcNow());
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await deleter.DeleteAsync(stored.WrittenKeys);
            throw;
        }

        return (await LoadDtoAsync(entryId, ct), null);
    }

    /// <summary>
    /// Removes a photo of the author's entry with its media item and stored objects. The error is
    /// the problem code care_not_found.
    /// </summary>
    public async Task<(CareEntryDto? Entry, string? Error)> RemoveAsync(
        long tankId, long entryId, long photoId, long profileId, CancellationToken ct)
    {
        List<string> storedKeys;
        await using (var transaction = await context.Database.BeginTransactionAsync(ct))
        {
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, "care_not_found");
            var entry = await CareEntryMapper.WithDetails(context.CareEntries)
                .SingleOrDefaultAsync(e => e.Id == entryId && e.TankId == tankId && e.AuthorProfileId == profileId, ct);
            var photo = entry?.Photos.SingleOrDefault(p => p.Id == photoId);
            if (entry is null || photo is null) return (null, "care_not_found");

            storedKeys = photo.MediaItem.Variants.Select(v => v.StorageKey).Append(photo.MediaItem.StorageKey).ToList();
            entry.Photos.Remove(photo);
            context.MediaItems.Remove(photo.MediaItem);
            CareEvents.Add(context, "care.updated", entry, clock.GetUtcNow());
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await deleter.DeleteAsync(storedKeys);
        return (await LoadDtoAsync(entryId, ct), null);
    }

    private async Task<int?> OwnPhotoCountAsync(long tankId, long entryId, long profileId, CancellationToken ct) =>
        await context.CareEntries.AsNoTracking()
            .Where(e => e.Id == entryId && e.TankId == tankId && e.AuthorProfileId == profileId
                && context.Tanks.Any(t => t.Id == tankId && t.DeletedAt == null))
            .Select(e => (int?)e.Photos.Count).SingleOrDefaultAsync(ct);

    private async Task<CareEntryDto> LoadDtoAsync(long entryId, CancellationToken ct) =>
        mapper.ToDto(await CareEntryMapper.WithDetails(context.CareEntries.AsNoTracking()).SingleAsync(e => e.Id == entryId, ct));
}
