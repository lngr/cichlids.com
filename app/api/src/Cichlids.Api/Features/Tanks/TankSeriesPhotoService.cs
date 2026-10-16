using System.Text.Json;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// Adds photos to the time-lapse series of a tank. A photo goes through the upload pipeline (same
/// formats, limits, capture time and public variants as other tank photos) and is stored as tank
/// media of the time-lapse section without a post. The media rows and the tank.photo_added event
/// commit in one transaction.
/// </summary>
public sealed class TankSeriesPhotoService(
    CichlidsDbContext context, UploadsWriteService uploads, StoredObjectDeleter deleter, IObjectStore objectStore, TimeProvider clock)
{
    public const string NotFoundCode = "tank_not_found";
    public const string InvalidCode = "tank_photo_invalid";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Stores the uploaded file as a new photo of the owner's tank series. The error is a problem
    /// code: tank_not_found, tank_photo_invalid or upload_rate_limited.
    /// </summary>
    public async Task<(TankTimeLapsePhotoDto? Photo, string? Error)> AddAsync(
        long tankId, long profileId, IFormFile? file, CancellationToken ct)
    {
        if (!await OwnsAsync(tankId, profileId, ct)) return (null, NotFoundCode);
        var (image, _) = await UploadedImageReader.ReadAsync(file, ct);
        if (image is null) return (null, InvalidCode);
        if (await uploads.CheckHourlyQuotaAsync(profileId, ct) is not null) return (null, UploadsEndpoints.UploadRateLimitedCode);

        StoredImage stored;
        try
        {
            stored = await uploads.StoreImageAsync(profileId, image.Bytes, image.Format, Path.GetFileName(file!.FileName), isPrivate: false, ct);
        }
        catch (Exception ex) when (ex is ImageProcessor.ImageTooLargeException or ImageProcessor.UnsupportedImageFormatException
            or ImageProcessor.BrokenImageException)
        {
            return (null, InvalidCode);
        }

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct))
            {
                await deleter.DeleteAsync(stored.WrittenKeys);
                return (null, NotFoundCode);
            }

            context.MediaItems.Add(stored.Item);
            await context.SaveChangesAsync(ct);
            var nextSort = (await context.TankMedia.Where(m => m.TankId == tankId && m.Section == TankMediaSection.TimeLapse)
                .MaxAsync(m => (int?)m.Sort, ct) ?? -1) + 1;
            context.TankMedia.Add(new TankMedia
            {
                TankId = tankId, MediaItemId = stored.Item.Id, Section = TankMediaSection.TimeLapse, Sort = nextSort,
            });
            context.OutboxEvents.Add(OutboxEvent.Create(
                "tank.photo_added", "tank", tankId.ToString(),
                JsonSerializer.Serialize(new { tankId, mediaItemId = stored.Item.Id, profileId }, PayloadOptions), clock.GetUtcNow()));
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await deleter.DeleteAsync(stored.WrittenKeys);
            throw;
        }

        var urls = new MediaUrlBuilder(objectStore).Build(stored.Item.Variants.ToDictionary(v => v.Label, v => v.StorageKey));
        return (new TankTimeLapsePhotoDto(stored.Item.Id, urls, stored.Item.TakenAt, stored.Item.CreatedAt), null);
    }

    private Task<bool> OwnsAsync(long tankId, long profileId, CancellationToken ct) =>
        context.Tanks.AsNoTracking().AnyAsync(t => t.Id == tankId && t.ProfileId == profileId && t.DeletedAt == null, ct);
}
