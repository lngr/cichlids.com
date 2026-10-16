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
/// formats, limits and capture time as other tank photos) and is linked to the tank in the time-lapse
/// role without a post. The variants are stored under the public prefix when the profile of the tank is
/// public and under the private prefix otherwise; the link marks the media item for placement, which
/// moves the variants when the visibility rule asks for the other area. The media item, its link and the
/// tank.photo_added event commit in one transaction.
/// </summary>
public sealed class TankSeriesPhotoService(
    CichlidsDbContext context, UploadsWriteService uploads, StoredObjectDeleter deleter, IObjectStore objectStore, TimeProvider clock,
    MediaUrlSigner? signer = null)
{
    public const string NotFoundCode = "tank_not_found";
    public const string InvalidCode = "tank_photo_invalid";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Stores the uploaded file as a new photo of the owner's tank series; capturedInApp tells whether the
    /// client took it with the camera of the app. The error is a problem
    /// code: tank_not_found, tank_photo_invalid or upload_rate_limited.
    /// </summary>
    public async Task<(TankTimeLapsePhotoDto? Photo, string? Error)> AddAsync(
        long tankId, long profileId, IFormFile? file, bool capturedInApp, CancellationToken ct)
    {
        if (await ProfileLevelOfOwnAsync(tankId, profileId, ct) is not { } level) return (null, NotFoundCode);
        var storedPublic = level == TankVisibility.Public;
        var (image, _) = await UploadedImageReader.ReadAsync(file, ct);
        if (image is null) return (null, InvalidCode);
        if (await uploads.CheckHourlyQuotaAsync(profileId, ct) is not null) return (null, UploadsEndpoints.UploadRateLimitedCode);

        StoredImage stored;
        try
        {
            stored = await uploads.StoreImageAsync(profileId, image.Bytes, image.Format, Path.GetFileName(file!.FileName), isPrivate: !storedPublic, ct, privateArea: "tank", capturedInApp: capturedInApp);
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
            var nextPosition = (await context.MediaLinks.Where(l => l.TankId == tankId && l.Role == MediaLinkRole.TimeLapse)
                .MaxAsync(l => (int?)l.Position, ct) ?? -1) + 1;
            context.MediaLinks.Add(MediaLink.ForTank(tankId, stored.Item.Id, MediaLinkRole.TimeLapse, nextPosition));
            context.OutboxEvents.Add(OutboxEvent.Create(
                "tank.photo_added", "tank", tankId.ToString(),
                JsonSerializer.Serialize(new { tankId, mediaItemId = stored.Item.Id, profileId, capturedInApp }, PayloadOptions), clock.GetUtcNow()));
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await deleter.DeleteAsync(stored.WrittenKeys);
            throw;
        }

        var anonymousVisible = (await context.AnonymousVisibleMediaItemIdsAsync([stored.Item.Id], clock.GetUtcNow(), ct)).Contains(stored.Item.Id);
        var generations = await context.VisibleMediaGenerationsAsync([stored.Item.Id], profileId, clock.GetUtcNow(), ct);
        var urls = new MediaUrlBuilder(objectStore, signer).Build(stored.Item.Variants, allowSigned: true, anonymousVisible,
            generations.TryGetValue(stored.Item.Id, out var generation) ? generation : null);
        return (new TankTimeLapsePhotoDto(stored.Item.Id, urls, stored.Item.TakenAt, stored.Item.CreatedAt), null);
    }

    private Task<TankVisibility?> ProfileLevelOfOwnAsync(long tankId, long profileId, CancellationToken ct) =>
        context.Tanks.AsNoTracking().Where(t => t.Id == tankId && t.ProfileId == profileId && t.DeletedAt == null)
            .Select(t => (TankVisibility?)t.ProfileVisibility).SingleOrDefaultAsync(ct);
}
