using System.Text.Json;
using Cichlids.Api.Features.Common;
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
/// The lifecycle of guided water change sessions: start, step marks, the two photos,
/// finish and cancel. A tank has at most one open session; an open session older than the
/// configured lifetime is removed with its photos. Finishing creates the water change care entry
/// with the duration, the evidence and the photos, and records the care and session events in the
/// same transaction. Problem codes: water_change_not_found, water_change_request_invalid,
/// care_photo_invalid, care_amount_invalid, care_request_invalid and upload_rate_limited.
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
        List<string> removedKeys;
        WaterChangeSession session;
        var created = false;
        await using (var transaction = await context.Database.BeginTransactionAsync(ct))
        {
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, "water_change_not_found");
            var now = Now();
            removedKeys = await RemoveExpiredAsync(context.WaterChangeSessions.Where(s => s.TankId == tankId), now, ct);
            var open = await context.WaterChangeSessions.Include(s => s.Steps).SingleOrDefaultAsync(s => s.TankId == tankId && s.FinishedAt == null, ct);
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

        await deleter.DeleteAsync(removedKeys);
        return (new WaterChangeStart(await ToDtoAsync(session, ct), created), null);
    }

    /// <summary>
    /// The open session of the member's tank.
    /// </summary>
    public async Task<(WaterChangeSessionDto? Session, string? Error)> CurrentAsync(long tankId, long profileId, CancellationToken ct)
    {
        List<string> removedKeys;
        WaterChangeSession? open;
        await using (var transaction = await context.Database.BeginTransactionAsync(ct))
        {
            if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, "water_change_not_found");
            removedKeys = await RemoveExpiredAsync(context.WaterChangeSessions.Where(s => s.TankId == tankId), Now(), ct);
            open = await context.WaterChangeSessions.Include(s => s.Steps).SingleOrDefaultAsync(s => s.TankId == tankId && s.FinishedAt == null, ct);
            await transaction.CommitAsync(ct);
        }

        await deleter.DeleteAsync(removedKeys);
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
    /// Stores a photo of the open session in the slot, replacing the photo that was there.
    /// </summary>
    public async Task<(WaterChangeSessionDto? Session, string? Error)> SetPhotoAsync(
        long tankId, long id, long profileId, string slot, IFormFile? file, bool capturedInApp, CancellationToken ct)
    {
        if (!SnakeCaseEnum.TryParse<WaterChangePhotoSlot>(slot, out var parsed)) return (null, "water_change_request_invalid");
        if (!await OwnsOpenAsync(tankId, id, profileId, ct)) return (null, "water_change_not_found");
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

        List<string> replacedKeys = [];
        WaterChangeSession? session;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            session = await LockOpenAsync(tankId, id, profileId, ct);
            if (session is null)
            {
                await deleter.DeleteAsync(stored.WrittenKeys);
                return (null, "water_change_not_found");
            }

            var previousId = parsed == WaterChangePhotoSlot.Drained ? session.DrainedMediaItemId : session.RefilledMediaItemId;
            context.MediaItems.Add(stored.Item);
            await context.SaveChangesAsync(ct);
            var now = Now();
            if (parsed == WaterChangePhotoSlot.Drained)
                (session.DrainedMediaItemId, session.DrainedPhotoAt, session.DrainedCapturedInApp) = (stored.Item.Id, now, capturedInApp);
            else
                (session.RefilledMediaItemId, session.RefilledPhotoAt, session.RefilledCapturedInApp) = (stored.Item.Id, now, capturedInApp);
            if (previousId is { } replaced)
            {
                var old = await context.MediaItems.Include(m => m.Variants).SingleAsync(m => m.Id == replaced, ct);
                replacedKeys = old.Variants.Select(v => v.StorageKey).Append(old.StorageKey).ToList();
                context.MediaItems.Remove(old);
            }
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await deleter.DeleteAsync(stored.WrittenKeys);
            throw;
        }

        await deleter.DeleteAsync(replacedKeys);
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
        var evidence = WaterChangeEvidences.Evaluate(AtOf(WaterChangeStep.DrainingStarted), AtOf(WaterChangeStep.RefillStarted), now, Settings.MinDuration,
            WaterChangeEvidences.CountInAppPhotos(
                session.DrainedMediaItemId is null ? null : session.DrainedCapturedInApp,
                session.RefilledMediaItemId is null ? null : session.RefilledCapturedInApp));
        var duration = (int)Math.Max(0, (now - session.StartedAt).TotalSeconds);
        var entry = new CareEntry
        {
            TankId = tankId, AuthorProfileId = profileId, Kind = CareKind.WaterChange, OccurredAt = now, RecordedAt = now,
            WaterChangeSessionId = session.Id, DurationSeconds = duration, Evidence = evidence.State, EvidenceMissing = [.. evidence.Missing],
        };
        CareWriteService.SetAmount(entry, amount);
        if (session.DrainedMediaItemId is { } drained) entry.Photos.Add(new CarePhoto { MediaItemId = drained, Position = 0 });
        if (session.RefilledMediaItemId is { } refilled) entry.Photos.Add(new CarePhoto { MediaItemId = refilled, Position = 1 });
        context.CareEntries.Add(entry);
        session.FinishedAt = now;
        session.DurationSeconds = duration;
        await context.SaveChangesAsync(ct);
        CareEvents.Add(context, "care.logged", entry, now);
        AddEvent("water_change.finished", session, now, new Dictionary<string, object?>
        {
            ["finishedAt"] = now, ["durationSeconds"] = duration, ["careEntryId"] = entry.Id,
            ["evidence"] = new { state = SnakeCaseEnum.ToSnakeCase(evidence.State), missing = evidence.Missing },
            ["drainedPhoto"] = session.DrainedMediaItemId is not null, ["refilledPhoto"] = session.RefilledMediaItemId is not null,
        });
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var stored = await CareEntryMapper.WithDetails(context.CareEntries.AsNoTracking()).SingleAsync(e => e.Id == entry.Id, ct);
        return (new WaterChangeFinishedDto(await ToDtoAsync(session, ct), mapper.ToDto(stored)), null);
    }

    /// <summary>
    /// Discards the open session with its photos.
    /// </summary>
    public async Task<string?> CancelAsync(long tankId, long id, long profileId, CancellationToken ct)
    {
        List<string> keys;
        await using (var transaction = await context.Database.BeginTransactionAsync(ct))
        {
            var session = await LockOpenAsync(tankId, id, profileId, ct);
            if (session is null) return "water_change_not_found";
            keys = await RemoveAsync([session], ct);
            await transaction.CommitAsync(ct);
        }

        await deleter.DeleteAsync(keys);
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
            List<string> keys;
            int removed;
            await using (var transaction = await context.Database.BeginTransactionAsync(ct))
            {
                var cutoff = Now() - Settings.SessionLifetime;
                var batch = await context.WaterChangeSessions.Where(s => s.FinishedAt == null && s.StartedAt <= cutoff).OrderBy(s => s.Id).Take(100).ToListAsync(ct);
                removed = batch.Count;
                keys = removed == 0 ? [] : await RemoveAsync(batch, ct);
                await transaction.CommitAsync(ct);
            }
            await deleter.DeleteAsync(keys);
            total += removed;
            if (removed < 100) return total;
        }
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
        return await context.WaterChangeSessions.Include(s => s.Steps)
            .SingleOrDefaultAsync(s => s.Id == id && s.TankId == tankId && s.ProfileId == profileId && s.FinishedAt == null && s.StartedAt > cutoff, ct);
    }

    private async Task<List<string>> RemoveExpiredAsync(IQueryable<WaterChangeSession> scope, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - Settings.SessionLifetime;
        var expired = await scope.Where(s => s.FinishedAt == null && s.StartedAt <= cutoff).ToListAsync(ct);
        return expired.Count == 0 ? [] : await RemoveAsync(expired, ct);
    }

    // Removes the sessions with their photo media items and returns the stored keys to delete after the commit.
    private async Task<List<string>> RemoveAsync(IReadOnlyCollection<WaterChangeSession> sessions, CancellationToken ct)
    {
        var ids = sessions.SelectMany(s => new[] { s.DrainedMediaItemId, s.RefilledMediaItemId }).OfType<long>().ToList();
        var items = await context.MediaItems.Include(m => m.Variants).Where(m => ids.Contains(m.Id)).ToListAsync(ct);
        var keys = items.SelectMany(m => m.Variants.Select(v => v.StorageKey).Append(m.StorageKey)).ToList();
        context.WaterChangeSessions.RemoveRange(sessions);
        context.MediaItems.RemoveRange(items);
        await context.SaveChangesAsync(ct);
        return keys;
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
        var ids = new[] { session.DrainedMediaItemId, session.RefilledMediaItemId }.OfType<long>().ToList();
        var items = await context.MediaItems.AsNoTracking().Include(m => m.Variants).Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        WaterChangePhotoDto? PhotoOf(long? itemId, DateTimeOffset? at, bool? inApp) => itemId is { } key && items.TryGetValue(key, out var item) && at is { } received
            ? new(mapper.UrlsOf(item), item.Width, item.Height, inApp ?? false, received, item.TakenAt) : null;

        var previousId = await context.WaterChangeSessions.AsNoTracking()
            .Where(p => p.TankId == session.TankId && p.Id != session.Id && p.FinishedAt != null && p.DrainedMediaItemId != null
                && (session.FinishedAt == null || p.FinishedAt < session.FinishedAt))
            .OrderByDescending(p => p.FinishedAt).Select(p => p.DrainedMediaItemId).FirstOrDefaultAsync(ct);
        var previous = previousId is { } previousKey
            ? await context.MediaItems.AsNoTracking().Include(m => m.Variants).SingleOrDefaultAsync(m => m.Id == previousKey, ct) : null;

        return new WaterChangeSessionDto(session.Id, session.TankId, session.StartedAt, session.StartedAt + Settings.SessionLifetime,
            session.FinishedAt, session.DurationSeconds,
            session.Steps.OrderBy(s => s.At).ThenBy(s => s.Id).Select(s => new WaterChangeStepDto(s.Step, s.State, s.At)).ToList(),
            PhotoOf(session.DrainedMediaItemId, session.DrainedPhotoAt, session.DrainedCapturedInApp),
            PhotoOf(session.RefilledMediaItemId, session.RefilledPhotoAt, session.RefilledCapturedInApp),
            previous is null ? null : mapper.UrlsOf(previous), (int)Settings.MinDuration.TotalSeconds);
    }
}
