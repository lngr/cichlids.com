using System.Text.Json;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Streaks;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Decommissioning;

/// <summary>
/// Decommissions a tank of a member and puts it back into operation, each with a photo that proves the
/// state of the tank, and reads the operating time with the history of the decommissionings. A photo goes
/// through the upload pipeline and is linked to the tank in the decommission or recommission role;
/// it counts when it comes from the camera of the app, its capture time lies near the moment the server
/// receives it and the same picture has not served as proof before. The photo is visible like the care
/// history of the tank. The row of the decommissioning, the media link and the outbox event commit in one
/// transaction.
/// </summary>
public sealed class TankDecommissioningService(
    CichlidsDbContext context, UploadsWriteService uploads, StoredObjectDeleter deleter, IObjectStore objectStore, TimeProvider clock,
    IOptions<WaterChangeOptions> waterChange, MediaUrlSigner? signer = null)
{
    public const string NotFoundCode = "tank_not_found";
    public const string PhotoInvalidCode = "decommission_photo_invalid";
    public const string PhotoNotInAppCode = "decommission_photo_not_in_app";
    public const string PhotoTimeCode = "decommission_photo_time";
    public const string PhotoReusedCode = "decommission_photo_reused";
    public const string ScopeInvalidCode = "decommission_scope_invalid";
    public const string ActiveCode = "decommission_active";
    public const string NotActiveCode = "decommission_not_active";
    public const string DateInvalidCode = "decommission_date_invalid";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Decommissions the tank from the given local date on. The error is a problem code.
    /// </summary>
    public async Task<(TankOperationDto? Operation, string? Error)> StartAsync(long tankId, long profileId, IFormFile? file, bool capturedInApp,
        string? scopeName, DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        if (await OwnHistoryPublicAsync(tankId, profileId, ct) is not { } historyPublic) return (null, NotFoundCode);
        if (!SnakeCaseEnum.TryParse<DecommissionScope>(scopeName, out var scope)) return (null, ScopeInvalidCode);
        if (await context.TankDecommissionings.AnyAsync(d => d.TankId == tankId && d.EndedAt == null, ct)) return (null, ActiveCode);
        var (stored, error) = await StoreProofAsync(profileId, file, capturedInApp, historyPublic, ct);
        if (stored is null) return (null, error);

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct))
            {
                await deleter.DeleteAsync(stored.WrittenKeys);
                return (null, NotFoundCode);
            }
            if (await context.TankDecommissionings.AnyAsync(d => d.TankId == tankId && d.EndedAt == null, ct))
            {
                await deleter.DeleteAsync(stored.WrittenKeys);
                return (null, ActiveCode);
            }

            var now = clock.GetUtcNow();
            await AttachAsync(tankId, stored, TankMediaSection.Decommission, ct);
            var row = new TankDecommissioning
            {
                TankId = tankId, Scope = scope, StartedAt = now, StartDate = today, StartMediaItemId = stored.Item.Id,
            };
            context.TankDecommissionings.Add(row);
            await context.SaveChangesAsync(ct);
            context.OutboxEvents.Add(OutboxEvent.Create("tank.decommissioned", "tank", tankId.ToString(),
                JsonSerializer.Serialize(new
                {
                    tankId, profileId, decommissioningId = row.Id, scope = SnakeCaseEnum.ToSnakeCase(scope),
                    startDate = row.StartDate.ToString("yyyy-MM-dd"), mediaItemId = stored.Item.Id,
                }, PayloadOptions), now));
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await deleter.DeleteAsync(stored.WrittenKeys);
            throw;
        }

        return (await ReadAsync(tankId, profileId, today, utcOffsetMinutes, ct), null);
    }

    /// <summary>
    /// Puts the decommissioned tank back into operation on the given local date. The error is a problem
    /// code.
    /// </summary>
    public async Task<(TankOperationDto? Operation, string? Error)> EndAsync(long tankId, long profileId, IFormFile? file, bool capturedInApp,
        DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        if (await OwnHistoryPublicAsync(tankId, profileId, ct) is not { } historyPublic) return (null, NotFoundCode);
        if (!await context.TankDecommissionings.AnyAsync(d => d.TankId == tankId && d.EndedAt == null, ct)) return (null, NotActiveCode);
        var (stored, error) = await StoreProofAsync(profileId, file, capturedInApp, historyPublic, ct);
        if (stored is null) return (null, error);

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct))
            {
                await deleter.DeleteAsync(stored.WrittenKeys);
                return (null, NotFoundCode);
            }
            var row = await context.TankDecommissionings.SingleOrDefaultAsync(d => d.TankId == tankId && d.EndedAt == null, ct);
            if (row is null)
            {
                await deleter.DeleteAsync(stored.WrittenKeys);
                return (null, NotActiveCode);
            }
            if (today < row.StartDate)
            {
                await deleter.DeleteAsync(stored.WrittenKeys);
                return (null, DateInvalidCode);
            }

            var now = clock.GetUtcNow();
            await AttachAsync(tankId, stored, TankMediaSection.Recommission, ct);
            row.EndedAt = now;
            row.EndDate = today;
            row.EndMediaItemId = stored.Item.Id;
            await context.SaveChangesAsync(ct);
            context.OutboxEvents.Add(OutboxEvent.Create("tank.recommissioned", "tank", tankId.ToString(),
                JsonSerializer.Serialize(new
                {
                    tankId, profileId, decommissioningId = row.Id, endDate = today.ToString("yyyy-MM-dd"), mediaItemId = stored.Item.Id,
                }, PayloadOptions), now));
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await deleter.DeleteAsync(stored.WrittenKeys);
            throw;
        }

        return (await ReadAsync(tankId, profileId, today, utcOffsetMinutes, ct), null);
    }

    /// <summary>
    /// The operating time and the decommissionings of a tank on a local date, with the photo URLs of the
    /// proof photos: public URLs for photos that anonymous viewers see and signed paths otherwise.
    /// </summary>
    public async Task<TankOperationDto> ReadAsync(long tankId, long? viewerProfileId, DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        var created = await context.Tanks.AsNoTracking().Where(t => t.Id == tankId).Select(t => t.CreatedAt).SingleAsync(ct);
        var rows = await context.TankDecommissionings.AsNoTracking().Where(d => d.TankId == tankId)
            .OrderByDescending(d => d.StartedAt).ThenByDescending(d => d.Id).ToListAsync(ct);
        var spans = rows.Select(r => new DecommissionSpan(r.StartDate, r.EndDate)).ToList();
        var operation = Decommissions.OperationOn(CarePlans.LocalDate(created, utcOffsetMinutes), spans, today);
        var itemIds = rows.SelectMany(r => new[] { (long?)r.StartMediaItemId, r.EndMediaItemId }).OfType<long>().Distinct().ToList();
        var variants = (await context.MediaVariants.AsNoTracking().Where(v => itemIds.Contains(v.MediaItemId)).ToListAsync(ct)).ToLookup(v => v.MediaItemId);
        var urls = new MediaUrlBuilder(objectStore, signer);
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(itemIds, clock.GetUtcNow(), ct);
        var generations = await context.VisibleMediaGenerationsAsync(itemIds, viewerProfileId, clock.GetUtcNow(), ct);
        ImageUrlsDto? Image(long? id) => id is { } key ? urls.Build(variants[key], allowSigned: true, anonymousVisible.Contains(key), generations.TryGetValue(key, out var generation) ? generation : null) : null;
        var items = rows.Select(r => new DecommissioningDto(r.Id, r.Scope, StreakService.Format(r.StartDate),
            r.EndDate is { } end ? StreakService.Format(end) : null, r.EndedAt is null, Image(r.StartMediaItemId), Image(r.EndMediaItemId))).ToList();
        return new TankOperationDto(tankId, operation.OperatingDays, operation.OperatingMonths, operation.DecommissionedDays,
            rows.Any(r => r.EndedAt is null), items);
    }

    // Whether the care history of the own tank is public, or null when the tank is not the member's own.
    private Task<bool?> OwnHistoryPublicAsync(long tankId, long profileId, CancellationToken ct) =>
        context.Tanks.AsNoTracking().Where(t => t.Id == tankId && t.ProfileId == profileId && t.DeletedAt == null)
            .Select(t => (bool?)(t.ProfileVisibility == TankVisibility.Public && t.StreamVisibility == TankVisibility.Public)).SingleOrDefaultAsync(ct);

    // Stores the photo and judges its provenance; a photo that does not qualify leaves nothing behind.
    private async Task<(StoredImage? Stored, string? Error)> StoreProofAsync(long profileId, IFormFile? file, bool capturedInApp,
        bool historyPublic, CancellationToken ct)
    {
        var (image, _) = await UploadedImageReader.ReadAsync(file, ct);
        if (image is null) return (null, PhotoInvalidCode);
        if (await uploads.CheckHourlyQuotaAsync(profileId, ct) is not null) return (null, UploadsEndpoints.UploadRateLimitedCode);
        StoredImage stored;
        try
        {
            stored = await uploads.StoreImageAsync(profileId, image.Bytes, image.Format, Path.GetFileName(file!.FileName),
                isPrivate: !historyPublic, ct, privateArea: "tank", capturedInApp: capturedInApp);
        }
        catch (Exception ex) when (ex is ImageProcessor.ImageTooLargeException or ImageProcessor.UnsupportedImageFormatException
            or ImageProcessor.BrokenImageException)
        {
            return (null, PhotoInvalidCode);
        }

        string? finding;
        try
        {
            var proof = new EvidencePhoto(capturedInApp, clock.GetUtcNow(), stored.Item.TakenAt, stored.Item.TakenAtOffsetMinutes is not null,
                await WasProofBeforeAsync(profileId, stored.Item.ChecksumSha256, ct));
            finding = DecommissionProofs.Check(proof, waterChange.Value.ClockTolerance);
        }
        catch
        {
            await deleter.DeleteAsync(stored.WrittenKeys);
            throw;
        }
        if (finding is null) return (stored, null);
        await deleter.DeleteAsync(stored.WrittenKeys);
        return (null, finding switch
        {
            DecommissionProofs.NotInApp => PhotoNotInAppCode,
            DecommissionProofs.TimeImplausible => PhotoTimeCode,
            _ => PhotoReusedCode,
        });
    }

    private async Task<bool> WasProofBeforeAsync(long profileId, string? checksum, CancellationToken ct)
    {
        if (checksum is null) return false;
        return await context.MediaLinks.AsNoTracking().AnyAsync(l => l.MediaItem.OwnerProfileId == profileId && l.MediaItem.ChecksumSha256 == checksum
            && (l.TankId != null && (l.Role == MediaLinkRole.Decommission || l.Role == MediaLinkRole.Recommission)
                || l.CareEntryId != null && l.Role == MediaLinkRole.Photo), ct);
    }

    // Adds the media item to the tank in the given section. The link marks the item for placement.
    private async Task AttachAsync(long tankId, StoredImage stored, TankMediaSection section, CancellationToken ct)
    {
        context.MediaItems.Add(stored.Item);
        await context.SaveChangesAsync(ct);
        var role = MediaLinkRoles.Of(section);
        var nextPosition = (await context.MediaLinks.Where(l => l.TankId == tankId && l.Role == role).MaxAsync(l => (int?)l.Position, ct) ?? -1) + 1;
        context.MediaLinks.Add(MediaLink.ForTank(tankId, stored.Item.Id, role, nextPosition));
    }
}
