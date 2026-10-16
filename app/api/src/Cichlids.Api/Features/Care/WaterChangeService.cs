using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// The lifecycle of guided water change sessions: start, step marks, the photos,
/// finish and cancel. A tank has at most one open session; an open session older than the
/// configured lifetime is removed with its photos. Finishing creates the water change care entry
/// with the duration, the evidence and the photos, and records the care and session events in the
/// same transaction. Problem codes: water_change_not_found, water_change_request_invalid,
/// care_photo_invalid, water_change_photo_limit, care_amount_invalid, care_request_invalid and upload_rate_limited.
/// </summary>
public sealed class WaterChangeService(
    CichlidsDbContext context, UploadsWriteService uploads, StoredObjectDeleter deleter, CareEntryMapper mapper,
    TimeProvider clock, IOptions<WaterChangeOptions> options)
{
    private WaterChangeOptions Settings => options.Value;

    /// <summary>
    /// Starts a session on the member's tank, or returns the open one.
    /// </summary>
    public async Task<(WaterChangeStart? Start, string? Error)> StartAsync(long tankId, long profileId, CancellationToken ct)
    {
        WaterChangeSession session;
        var created = false;
        await using (var transaction = await context.Database.BeginTransactionAsync(ct))
        {
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, "water_change_not_found");
            var now = Now();
            await RemoveExpiredAsync(context.WaterChangeSessions.Where(s => s.TankId == tankId), now, ct);
            var open = await context.WaterChangeSessions.Include(s => s.Steps).Include(s => s.MediaLinks).SingleOrDefaultAsync(s => s.TankId == tankId && s.FinishedAt == null, ct);
            if (open is null)
            {
                open = new WaterChangeSession { TankId = tankId, ProfileId = profileId, StartedAt = now };
                context.WaterChangeSessions.Add(open);
                await context.SaveChangesAsync(ct);
                AddEvent("water_change.started", open, now, new Dictionary<string, object?>());
                await context.SaveChangesAsync(ct);
                created = true;
            }
            await transaction.CommitAsync(ct);
            session = open;
        }

        return (new WaterChangeStart(await ToDtoAsync(session, ct), created), null);
    }

    /// <summary>
    /// The open session of the member's tank.
    /// </summary>
    public async Task<(WaterChangeSessionDto? Session, string? Error)> CurrentAsync(long tankId, long profileId, CancellationToken ct)
    {
        WaterChangeSession? open;
        await using (var transaction = await context.Database.BeginTransactionAsync(ct))
        {
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, "water_change_not_found");
            await RemoveExpiredAsync(context.WaterChangeSessions.Where(s => s.TankId == tankId), Now(), ct);
            open = await context.WaterChangeSessions.Include(s => s.Steps).Include(s => s.MediaLinks).SingleOrDefaultAsync(s => s.TankId == tankId && s.FinishedAt == null, ct);
            await transaction.CommitAsync(ct);
        }

        return open is null ? (null, "water_change_not_found") : (await ToDtoAsync(open, ct), null);
    }

    /// <summary>
    /// Records a step of the open session as done or skipped at the server time; a later mark of the
    /// same step replaces the earlier one.
    /// </summary>
    public async Task<(WaterChangeSessionDto? Session, string? Error)> MarkStepAsync(
        long tankId, long id, long profileId, string step, string stateName, CancellationToken ct)
    {
        if (!SnakeCaseEnum.TryParse<WaterChangeStep>(step, out var parsed) || !SnakeCaseEnum.TryParse<WaterChangeStepState>(stateName, out var state)) return (null, "water_change_request_invalid");
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var session = await LockOpenAsync(tankId, id, profileId, ct);
        if (session is null) return (null, "water_change_not_found");
        var now = Now();
        var existing = session.Steps.SingleOrDefault(s => s.Step == parsed);
        if (existing is null) session.Steps.Add(new WaterChangeSessionStep { Step = parsed, State = state, At = now });
        else (existing.State, existing.At) = (state, now);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await ToDtoAsync(session, ct), null);
    }

    /// <summary>
    /// Appends a photo to the open session, up to the maximum number of photos per session.
    /// </summary>
    public async Task<(WaterChangeSessionDto? Session, string? Error)> AddPhotoAsync(
        long tankId, long id, long profileId, IFormFile? file, bool capturedInApp, CancellationToken ct)
    {
        if (!await OwnsOpenAsync(tankId, id, profileId, ct)) return (null, "water_change_not_found");
        var (image, _) = await UploadedImageReader.ReadAsync(file, ct);
        if (image is null) return (null, "care_photo_invalid");
        if (await uploads.CheckHourlyQuotaAsync(profileId, ct) is not null) return (null, "upload_rate_limited");

        StoredImage stored;
        try
        {
            stored = await uploads.StoreImageAsync(profileId, image.Bytes, image.Format, Path.GetFileName(file!.FileName), isPrivate: true, ct, capturedInApp: capturedInApp);
        }
        catch (Exception ex) when (ex is ImageProcessor.ImageTooLargeException or ImageProcessor.UnsupportedImageFormatException
            or ImageProcessor.BrokenImageException)
        {
            return (null, "care_photo_invalid");
        }

        WaterChangeSession? session;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            session = await LockOpenAsync(tankId, id, profileId, ct);
            if (session is null || session.MediaLinks.Count >= MediaLinkLimits.EvidencePhotosPerSession)
            {
                await deleter.DeleteAsync(stored.WrittenKeys);
                return (null, session is null ? "water_change_not_found" : "water_change_photo_limit");
            }

            context.MediaItems.Add(stored.Item);
            await context.SaveChangesAsync(ct);
            session.MediaLinks.Add(MediaLink.ForWaterChangeSession(session.Id, stored.Item.Id,
                session.MediaLinks.Count == 0 ? 0 : session.MediaLinks.Max(l => l.Position) + 1));
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await deleter.DeleteAsync(stored.WrittenKeys);
            throw;
        }

        return (await ToDtoAsync(session, ct), null);
    }

    /// <summary>
    /// Finishes the open session: creates the water change entry with the duration, the evidence, the
    /// optional amount and the photos, and records the care and session events in the same transaction.
    /// </summary>
    public async Task<(WaterChangeFinishedDto? Finished, string? Error)> FinishAsync(
        long tankId, long id, long profileId, CareAmountRequest? amount, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var session = await LockOpenAsync(tankId, id, profileId, ct);
        if (session is null) return (null, "water_change_not_found");
        if (CareWriteService.ValidateAmount(amount) is { } amountError) return (null, amountError);

        var now = CareWriteService.AtMicrosecondPrecision(Now());
        DateTimeOffset? AtOf(WaterChangeStep step) =>
            session.Steps.SingleOrDefault(s => s.Step == step && s.State == WaterChangeStepState.Done)?.At;
        var evidence = WaterChangeEvidences.Evaluate(session.StartedAt, AtOf(WaterChangeStep.DrainingStarted), AtOf(WaterChangeStep.RefillStarted),
            now, Settings.MinDuration, Settings.ClockTolerance, await EvidencePhotosOfAsync(session, profileId, ct));
        var duration = (int)Math.Max(0, (now - session.StartedAt).TotalSeconds);
        var entry = new CareEntry
        {
            TankId = tankId, AuthorProfileId = profileId, Kind = CareKind.WaterChange, OccurredAt = now, RecordedAt = now,
            WaterChangeSessionId = session.Id, DurationSeconds = duration, Evidence = evidence.State, EvidenceMissing = [.. evidence.Missing],
        };
        CareWriteService.SetAmount(entry, amount);
        foreach (var link in session.MediaLinks.OrderBy(l => l.Position))
            entry.MediaLinks.Add(new MediaLink
            {
                ContextType = MediaLinkContext.CareEntry, Role = MediaLinkRole.Photo, MediaItemId = link.MediaItemId, Position = entry.MediaLinks.Count,
            });
        context.CareEntries.Add(entry);
        session.FinishedAt = now;
        session.DurationSeconds = duration;
        await context.SaveChangesAsync(ct);
        var evidenceIds = session.MediaLinks.Select(l => l.MediaItemId).ToList();
        var checksums = await context.MediaItems.AsNoTracking().Where(m => evidenceIds.Contains(m.Id))
            .Select(m => m.ChecksumSha256).ToListAsync(ct);
        await context.RecordUsedAsync(profileId, UsedChecksumKind.WaterChangeEvidence, checksums, now, ct);
        await context.RecordUsedAsync(profileId, UsedChecksumKind.Proof, checksums, now, ct);
        CareEvents.Add(context, "care.logged", entry, now);
        AddEvent("water_change.finished", session, now, new Dictionary<string, object?>
        {
            ["finishedAt"] = now, ["durationSeconds"] = duration, ["careEntryId"] = entry.Id,
            ["evidence"] = new { state = SnakeCaseEnum.ToSnakeCase(evidence.State), missing = evidence.Missing },
            ["photoCount"] = session.MediaLinks.Count,
        });
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var stored = await CareEntryMapper.WithDetails(context.CareEntries.AsNoTracking()).SingleAsync(e => e.Id == entry.Id, ct);
        await mapper.PrepareAsync([stored], profileId, ct);
        return (new WaterChangeFinishedDto(await ToDtoAsync(session, ct), mapper.ToDto(stored)), null);
    }

    /// <summary>
    /// Discards the open session and the links of its photos. The photos stay in the media library of the
    /// member.
    /// </summary>
    public async Task<string?> CancelAsync(long tankId, long id, long profileId, CancellationToken ct)
    {
        await using (var transaction = await context.Database.BeginTransactionAsync(ct))
        {
            var session = await LockOpenAsync(tankId, id, profileId, ct);
            if (session is null) return "water_change_not_found";
            await RemoveAsync([session], ct);
            await transaction.CommitAsync(ct);
        }

        return null;
    }

    /// <summary>
    /// Removes the open sessions of all tanks that are older than the lifetime, in batches, and returns
    /// how many were removed.
    /// </summary>
    public async Task<int> ExpireAllAsync(CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            int removed;
            await using (var transaction = await context.Database.BeginTransactionAsync(ct))
            {
                var cutoff = Now() - Settings.SessionLifetime;
                var batch = await context.WaterChangeSessions.Where(s => s.FinishedAt == null && s.StartedAt <= cutoff).OrderBy(s => s.Id).Take(100).ToListAsync(ct);
                removed = batch.Count;
                if (removed > 0) await RemoveAsync(batch, ct);
                await transaction.CommitAsync(ct);
            }
            total += removed;
            if (removed < 100) return total;
        }
    }

    // A picture counts as used before when its checksum was recorded as evidence of an earlier finished
    // session of the member, whatever happened to the links and the file since. The capture source and
    // the receive time are those of the media item.
    private async Task<List<EvidencePhoto>> EvidencePhotosOfAsync(WaterChangeSession session, long profileId, CancellationToken ct)
    {
        var ids = session.MediaLinks.Select(l => l.MediaItemId).ToList();
        var items = await context.MediaItems.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var checksums = items.Values.Select(m => m.ChecksumSha256).OfType<string>().Distinct().ToList();
        var used = await context.UsedAmongAsync(profileId, UsedChecksumKind.WaterChangeEvidence, checksums, ct);
        return session.MediaLinks.Where(l => items.ContainsKey(l.MediaItemId)).Select(l =>
        {
            var item = items[l.MediaItemId];
            return new EvidencePhoto(item.CapturedInApp, item.ReceivedAt, item.TakenAt, item.TakenAtOffsetMinutes is not null,
                item.ChecksumSha256 is { } sum && used.Contains(sum));
        }).ToList();
    }

    private DateTimeOffset Now() => clock.GetUtcNow();

    private async Task<bool> OwnsOpenAsync(long tankId, long id, long profileId, CancellationToken ct) =>
        await context.WaterChangeSessions.AsNoTracking().AnyAsync(s => s.Id == id && s.TankId == tankId && s.ProfileId == profileId
            && s.FinishedAt == null && s.StartedAt > Now() - Settings.SessionLifetime
            && context.Tanks.Any(t => t.Id == tankId && t.DeletedAt == null), ct);

    // Locks the tank of the member and loads the open, unexpired session with its steps.
    private async Task<WaterChangeSession?> LockOpenAsync(long tankId, long id, long profileId, CancellationToken ct)
    {
        if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return null;
        var cutoff = Now() - Settings.SessionLifetime;
        return await context.WaterChangeSessions.Include(s => s.Steps).Include(s => s.MediaLinks)
            .SingleOrDefaultAsync(s => s.Id == id && s.TankId == tankId && s.ProfileId == profileId && s.FinishedAt == null && s.StartedAt > cutoff, ct);
    }

    private async Task RemoveExpiredAsync(IQueryable<WaterChangeSession> scope, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - Settings.SessionLifetime;
        var expired = await scope.Where(s => s.FinishedAt == null && s.StartedAt <= cutoff).ToListAsync(ct);
        if (expired.Count > 0) await RemoveAsync(expired, ct);
    }

    // Removes the sessions with the links of their photos. The photos stay in the media library.
    private async Task RemoveAsync(IReadOnlyCollection<WaterChangeSession> sessions, CancellationToken ct)
    {
        context.WaterChangeSessions.RemoveRange(sessions);
        await context.SaveChangesAsync(ct);
    }

    private void AddEvent(string type, WaterChangeSession session, DateTimeOffset now, Dictionary<string, object?> extra)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sessionId"] = session.Id, ["tankId"] = session.TankId, ["profileId"] = session.ProfileId, ["startedAt"] = session.StartedAt,
        };
        foreach (var (key, value) in extra) payload[key] = value;
        context.OutboxEvents.Add(OutboxEvent.Create(type, "water_change", session.Id.ToString(), JsonSerializer.Serialize(payload), now));
    }

    private async Task<WaterChangeSessionDto> ToDtoAsync(WaterChangeSession session, CancellationToken ct)
    {
        var photos = session.MediaLinks.OrderBy(l => l.Position).ToList();
        var ids = photos.Select(l => l.MediaItemId).ToList();
        var items = await context.MediaItems.AsNoTracking().Include(m => m.Variants).Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        await mapper.PrepareAsync(ids, session.ProfileId, ct);

        var previousSessionId = await context.WaterChangeSessions.AsNoTracking()
            .Where(p => p.TankId == session.TankId && p.Id != session.Id && p.FinishedAt != null && p.MediaLinks.Any()
                && (session.FinishedAt == null || p.FinishedAt < session.FinishedAt))
            .OrderByDescending(p => p.FinishedAt).Select(p => (long?)p.Id).FirstOrDefaultAsync(ct);
        List<CarePhotoUrlsDto> previous = [];
        if (previousSessionId is { } previousKey)
        {
            var previousIds = await context.MediaLinks.AsNoTracking().EvidenceLinks().Where(l => l.WaterChangeSessionId == previousKey)
                .OrderBy(l => l.Position).Select(l => l.MediaItemId).ToListAsync(ct);
            var previousItems = await context.MediaItems.AsNoTracking().Include(m => m.Variants).Where(m => previousIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
            await mapper.PrepareAsync(previousIds, session.ProfileId, ct);
            previous = previousIds.Where(previousItems.ContainsKey).Select(key => mapper.UrlsOf(previousItems[key])).ToList();
        }

        return new WaterChangeSessionDto(session.Id, session.TankId, session.StartedAt, session.StartedAt + Settings.SessionLifetime,
            session.FinishedAt, session.DurationSeconds,
            session.Steps.OrderBy(s => s.At).ThenBy(s => s.Id).Select(s => new WaterChangeStepDto(s.Step, s.State, s.At)).ToList(),
            photos.Where(l => items.ContainsKey(l.MediaItemId)).Select(l => new WaterChangePhotoDto(l.MediaItemId, mapper.UrlsOf(items[l.MediaItemId]),
                items[l.MediaItemId].Width, items[l.MediaItemId].Height, items[l.MediaItemId].CapturedInApp, items[l.MediaItemId].ReceivedAt,
                items[l.MediaItemId].TakenAt)).ToList(),
            previous, (int)Settings.MinDuration.TotalSeconds);
    }
}
