using Cichlids.Api.Features.Videos;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// Places the stored media of a tank (photo variants, time-lapse photos and the HLS files of videos)
/// under the public or the private storage prefix, following the profile level of the tank: only a
/// public profile keeps its media under the public prefix, where anyone can read them by URL. A tank
/// whose placement is due carries MediaMovePending; the tank row is the queue. A worker claims one due
/// tank at a time with a lease (row lock with SKIP LOCKED), so several workers on several instances
/// never place the same tank. The placement is idempotent and resumable: every pass works from the
/// stored keys, copies an object only when its target is missing, updates the variant rows of one item
/// in one transaction and deletes the objects of the opposite placement afterwards, including
/// leftovers of an interrupted pass. A pass repeats until the level of the tank did not change while
/// it ran, and only then clears the pending mark under the row lock. A failed pass is retried with a
/// growing delay, logged as a warning and, from the fifth failed attempt, as an error. The pending mark
/// is cleared only under the row lock of the tank after a verification inside the lock found every
/// variant on the side the level asks for and no object left on the other side, so media that arrive
/// while a pass runs (an upload or a transcode that commits later) keep the tank pending until they are
/// placed too. Each object is made unreachable at its old place right after its copy and the repointing
/// of its row, one variant at a time, to keep the time an object of a tank that turned private stays
/// readable by its public URL as short as the copy of that single object. Media items that a post also
/// uses stay where they are, since a published picture has its own lifecycle. The originals are private
/// in both placements and never move.
///
/// Public variant objects carry no Cache-Control header of their own, so a cache or CDN in front of the
/// bucket may keep serving a copy of an object after it was deleted, until its own expiry. The URL kind
/// therefore follows the tank, not the place of the object, and an operator who puts a cache in front of
/// the public prefix chooses its lifetime as the time a tank that turned private can stay visible there;
/// a short lifetime costs cache hits on the public archive, a long one extends that time.
/// </summary>
public sealed class TankMediaPlacementService(
    CichlidsDbContext context,
    IObjectStore store,
    TimeProvider clock,
    IOptions<TankMediaPlacementOptions> options,
    ILogger<TankMediaPlacementService> logger)
{
    private const int MaxPassesPerRun = 5;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    /// <summary>
    /// Claims and places the next due tank. Returns false when no tank was due.
    /// </summary>
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken, long? onlyTankId = null)
    {
        var tankId = await ClaimAsync(cancellationToken, onlyTankId);
        if (tankId is null)
        {
            return false;
        }

        try
        {
            await PlaceAsync(tankId.Value, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The lease is released so that the next worker continues at once.
            await context.Tanks.Where(t => t.Id == tankId).ExecuteUpdateAsync(
                s => s.SetProperty(t => t.MediaMoveNextAttemptAt, clock.GetUtcNow()), CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await FailAsync(tankId.Value, ex);
        }

        return true;
    }

    /// <summary>
    /// Places the media of one tank within the given time budget, for a caller that wants the media of a
    /// tank that just turned private out of the public prefix before it answers. When the budget is used
    /// up or the placement fails, the tank stays pending and the worker continues.
    /// </summary>
    public async Task PlaceNowAsync(long tankId, TimeSpan budget)
    {
        using var timeout = new CancellationTokenSource(budget);
        try
        {
            await RunOnceAsync(timeout.Token, tankId);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Placing the media of tank {TankId} continues in the background.", tankId);
        }
    }

    private async Task<long?> ClaimAsync(CancellationToken cancellationToken, long? onlyTankId)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var ids = await context.Database.SqlQuery<long>($"""
            SELECT id AS "Value" FROM public.tank
            WHERE media_move_pending AND (media_move_next_attempt_at IS NULL OR media_move_next_attempt_at <= {now})
                AND ({onlyTankId}::bigint IS NULL OR id = {onlyTankId}::bigint)
            ORDER BY media_move_next_attempt_at NULLS FIRST, id
            FOR UPDATE SKIP LOCKED
            LIMIT 1
            """).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return null;
        }

        var leaseEnd = now + options.Value.Lease;
        await context.Tanks.Where(t => t.Id == ids[0])
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MediaMoveNextAttemptAt, leaseEnd), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ids[0];
    }

    private async Task FailAsync(long tankId, Exception exception)
    {
        var now = clock.GetUtcNow();
        var attempts = await context.Tanks.Where(t => t.Id == tankId).Select(t => t.MediaMoveAttempts).SingleAsync(CancellationToken.None) + 1;
        if (attempts >= 5)
        {
            logger.LogError(exception, "Placing the media of tank {TankId} failed {Attempts} times; the media may still lie under the wrong prefix.", tankId, attempts);
        }
        else
        {
            logger.LogWarning(exception, "Placing the media of tank {TankId} failed (attempt {Attempts}); it is attempted again.", tankId, attempts);
        }

        var delay = TimeSpan.FromTicks(Math.Min(options.Value.RetryDelay.Ticks * attempts, MaxRetryDelay.Ticks));
        await context.Tanks.Where(t => t.Id == tankId).ExecuteUpdateAsync(
            s => s.SetProperty(t => t.MediaMoveAttempts, attempts).SetProperty(t => t.MediaMoveNextAttemptAt, now + delay), CancellationToken.None);
    }

    private async Task PlaceAsync(long tankId, CancellationToken cancellationToken)
    {
        for (var pass = 0; pass < MaxPassesPerRun; pass++)
        {
            context.ChangeTracker.Clear();
            var level = await context.Tanks.AsNoTracking().Where(t => t.Id == tankId).Select(t => t.ProfileVisibility)
                .SingleAsync(cancellationToken);
            var toPublic = TankVisibilityRules.MediaBelongsInPublicStorage(level);
            foreach (var item in await MovableItemsAsync(tankId, cancellationToken))
            {
                await PlaceItemAsync(item, toPublic, cancellationToken);
            }

            if (await TryFinishAsync(tankId, toPublic, cancellationToken))
            {
                return;
            }
        }

        // The level or the media kept changing while the passes ran; the tank stays pending and is attempted again.
        await FailAsync(tankId, new InvalidOperationException("The placement did not settle within one run."));
    }

    private async Task<bool> TryFinishAsync(long tankId, bool placedPublic, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tank = await context.Tanks.FromSqlInterpolated($"SELECT * FROM public.tank WHERE id = {tankId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        if (TankVisibilityRules.MediaBelongsInPublicStorage(tank.ProfileVisibility) != placedPublic
            || !await IsSettledAsync(tankId, placedPublic, cancellationToken))
        {
            return false;
        }

        tank.MediaPlacedPublic = placedPublic;
        tank.MediaMovePending = false;
        tank.MediaMoveAttempts = 0;
        tank.MediaMoveNextAttemptAt = null;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Whether every movable variant lies on the side the placement asks for and no object is left on the
    /// other side. Runs under the row lock of the tank, so media that commit later cannot slip past.
    /// </summary>
    private async Task<bool> IsSettledAsync(long tankId, bool toPublic, CancellationToken cancellationToken)
    {
        foreach (var item in await MovableItemsAsync(tankId, cancellationToken))
        {
            if (item.Kind == MediaKind.Video)
            {
                if (item.Variants.Any(v => v.StorageKey.StartsWith(OppositeDirectory(item, toPublic), StringComparison.Ordinal)))
                {
                    return false;
                }

                await foreach (var _ in store.ListKeysAsync(OppositeDirectory(item, toPublic), cancellationToken))
                {
                    return false;
                }

                continue;
            }

            foreach (var variant in item.Variants.Where(v =>
                MediaStorageKeys.IsPublicVariantKey(v.StorageKey) || MediaStorageKeys.IsPrivateVariantKey(v.StorageKey)))
            {
                if (variant.StorageKey != TargetOf(variant.StorageKey, toPublic)
                    || await store.ExistsAsync(TargetOf(variant.StorageKey, !toPublic), cancellationToken))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string OppositeDirectory(MediaItem item, bool toPublic) =>
        MediaStorageKeys.BuildVideoDirectory(item.StorageKey, isPrivate: toPublic);

    private async Task<List<MediaItem>> MovableItemsAsync(long tankId, CancellationToken cancellationToken)
    {
        var tankItems = context.MediaLinks.Where(l => l.TankId == tankId).Select(l => l.MediaItemId);
        return await context.MediaItems.Include(m => m.Variants)
            .Where(m => tankItems.Contains(m.Id) && !context.MediaLinks.Any(l => l.PostId != null && l.MediaItemId == m.Id))
            .OrderBy(m => m.Id).ToListAsync(cancellationToken);
    }

    private async Task PlaceItemAsync(MediaItem item, bool toPublic, CancellationToken cancellationToken)
    {
        if (item.Kind == MediaKind.Video)
        {
            await PlaceVideoAsync(item, toPublic, cancellationToken);
            return;
        }

        foreach (var variant in item.Variants
            .Where(v => MediaStorageKeys.IsPublicVariantKey(v.StorageKey) || MediaStorageKeys.IsPrivateVariantKey(v.StorageKey)).ToList())
        {
            var target = TargetOf(variant.StorageKey, toPublic);
            var opposite = TargetOf(variant.StorageKey, !toPublic);
            if (variant.StorageKey != target)
            {
                await CopyAsync(variant.StorageKey, target, cancellationToken);
                variant.StorageKey = target;
                await context.SaveChangesAsync(cancellationToken);
            }

            await store.DeleteAsync(opposite, cancellationToken);
        }
    }

    private async Task PlaceVideoAsync(MediaItem item, bool toPublic, CancellationToken cancellationToken)
    {
        if (item.Variants.All(v => v.Label is not (VideoStorage.HlsLabel or VideoStorage.PosterLabel)))
        {
            return;
        }

        var publicDirectory = MediaStorageKeys.BuildVideoDirectory(item.StorageKey, isPrivate: false);
        var privateDirectory = MediaStorageKeys.BuildVideoDirectory(item.StorageKey, isPrivate: true);
        var (sourceDirectory, targetDirectory) = toPublic ? (privateDirectory, publicDirectory) : (publicDirectory, privateDirectory);
        await foreach (var key in store.ListKeysAsync(sourceDirectory, cancellationToken))
        {
            await CopyAsync(key, targetDirectory + key[sourceDirectory.Length..], cancellationToken);
        }

        await RepointAsync(item, v => v.StorageKey.StartsWith(sourceDirectory, StringComparison.Ordinal)
            ? targetDirectory + v.StorageKey[sourceDirectory.Length..] : v.StorageKey, cancellationToken);
        var leftovers = new List<string>();
        await foreach (var key in store.ListKeysAsync(sourceDirectory, cancellationToken))
        {
            leftovers.Add(key);
        }

        foreach (var key in leftovers)
        {
            await store.DeleteAsync(key, cancellationToken);
        }
    }

    private static string TargetOf(string key, bool toPublic) => toPublic
        ? MediaStorageKeys.IsPrivateVariantKey(key) ? MediaStorageKeys.ToPublicVariantKey(key) : key
        : MediaStorageKeys.IsPublicVariantKey(key) ? MediaStorageKeys.ToPrivateVariantKey(key) : key;

    private async Task RepointAsync(MediaItem item, Func<MediaVariant, string> targetOf, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var variant in item.Variants)
        {
            var target = targetOf(variant);
            if (target != variant.StorageKey)
            {
                variant.StorageKey = target;
                changed = true;
            }
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Copies an object unless the target exists. A missing source with a missing target is an error,
    /// since the stored object is gone.
    /// </summary>
    private async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        if (await store.ExistsAsync(target, cancellationToken))
        {
            return;
        }

        var head = await store.HeadAsync(source, cancellationToken)
            ?? throw new InvalidOperationException($"The object '{source}' to place is missing.");
        await using var download = await store.GetAsync(source, cancellationToken)
            ?? throw new InvalidOperationException($"The object '{source}' to place is missing.");
        // The upload needs a seekable body; photo variants and HLS segments are small enough to buffer.
        await using var content = new MemoryStream(checked((int)head.Size));
        await download.CopyToAsync(content, cancellationToken);
        var contentType = target.Contains("variants/video/", StringComparison.Ordinal)
            ? MediaStorageKeys.ResolveHlsContentType(target)
            : MediaStorageKeys.ResolveContentType(target);
        await store.PutAsync(target, content, contentType, head.Metadata, cancellationToken);
    }
}

/// <summary>
/// Places the media of tanks for the lifetime of the host, one tank after the other. When no tank is due
/// the loop waits for the poll interval.
/// </summary>
public sealed class TankMediaPlacementHostedService(
    IServiceScopeFactory scopeFactory, IOptions<TankMediaPlacementOptions> options, ILogger<TankMediaPlacementHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var placed = false;
            try
            {
                using var scope = scopeFactory.CreateScope();
                placed = await scope.ServiceProvider.GetRequiredService<TankMediaPlacementService>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A tank media placement pass failed.");
            }

            if (placed)
            {
                continue;
            }

            try
            {
                await Task.Delay(options.Value.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
