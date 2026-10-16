using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Media;

/// <summary>
/// Places the stored variants of a media item (the photo sizes or the HLS files of a video) under the
/// public or the private storage prefix, following the visibility rule: only an item that anonymous
/// viewers see keeps its variants under the public prefix, where anyone can read them by URL. An item
/// whose placement is due carries PlacementPending; the media item row is the queue. A worker claims one
/// due item at a time with a lease (row lock with SKIP LOCKED), so several workers on several instances
/// never place the same item. The placement is idempotent and resumable: every pass works from the stored
/// keys, copies an object only when its target is missing, updates the variant rows of the item in one
/// transaction and deletes the objects of the opposite placement afterwards, including leftovers of an
/// interrupted pass. A pass repeats until the answer of the visibility rule did not change while it ran,
/// and only then clears the pending mark under the row lock. A failed pass is retried with a growing
/// delay, logged as a warning and, from the fifth failed attempt, as an error. The pending mark is cleared
/// only under the row lock of the item after a verification inside the lock found every variant on the
/// side the rule asks for and no object left on the other side. The originals are private in both
/// placements and never move.
///
/// A reconcile pass compares the recorded placement of every item with the answer of the visibility rule
/// and marks the items that differ, so a change that marked nothing is corrected as well.
///
/// Public variant objects carry no Cache-Control header of their own, so a cache or CDN in front of the
/// bucket may keep serving a copy of an object after it was deleted, until its own expiry. The URL kind
/// follows the visibility rule, not the place of the object, and an operator who puts a cache in front of
/// the public prefix chooses its lifetime as the time a media item that turned private can stay visible
/// there; a short lifetime costs cache hits on the public archive, a long one extends that time.
/// </summary>
public sealed class MediaPlacementService(
    CichlidsDbContext context,
    IObjectStore store,
    TimeProvider clock,
    IOptions<MediaPlacementOptions> options,
    ILogger<MediaPlacementService> logger)
{
    private const int MaxPassesPerRun = 5;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    /// <summary>
    /// Claims and places the next due media item, or the next due one among the given ids. Returns false
    /// when no item was due.
    /// </summary>
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken, IReadOnlyCollection<long>? onlyItemIds = null)
    {
        var itemId = await ClaimAsync(cancellationToken, onlyItemIds);
        if (itemId is null)
        {
            return false;
        }

        try
        {
            await PlaceAsync(itemId.Value, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The lease is released so that the next worker continues at once.
            await context.MediaItems.Where(m => m.Id == itemId).ExecuteUpdateAsync(
                s => s.SetProperty(m => m.PlacementNextAttemptAt, clock.GetUtcNow()), CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await FailAsync(itemId.Value, ex);
        }

        return true;
    }

    /// <summary>
    /// Marks the media items whose recorded placement differs from the visibility rule, up to the batch
    /// size. Returns the number of marked items.
    /// </summary>
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var visible = context.AnonymousVisibleMediaItems(now).Select(m => m.Id);
        var ids = await context.MediaItems.AsNoTracking()
            .Where(m => !m.PlacementPending && m.PlacedPublic != visible.Contains(m.Id))
            .OrderBy(m => m.Id).Select(m => m.Id).Take(Math.Max(1, options.Value.ReconcileBatchSize))
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        logger.LogInformation("Marking {Count} media items whose placement differs from the visibility rule.", ids.Count);
        return await context.MarkForPlacementAsync(context.MediaItems.Where(m => ids.Contains(m.Id)).Select(m => m.Id), cancellationToken);
    }

    private async Task<long?> ClaimAsync(CancellationToken cancellationToken, IReadOnlyCollection<long>? onlyItemIds)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var ids = onlyItemIds is null
            ? await context.Database.SqlQuery<long>($"""
                SELECT id AS "Value" FROM public.media_item
                WHERE placement_pending AND (placement_next_attempt_at IS NULL OR placement_next_attempt_at <= {now})
                ORDER BY placement_next_attempt_at NULLS FIRST, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """).ToListAsync(cancellationToken)
            : await context.Database.SqlQuery<long>($"""
                SELECT id AS "Value" FROM public.media_item
                WHERE placement_pending AND (placement_next_attempt_at IS NULL OR placement_next_attempt_at <= {now})
                    AND id = ANY({onlyItemIds.ToArray()})
                ORDER BY placement_next_attempt_at NULLS FIRST, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return null;
        }

        var leaseEnd = now + options.Value.Lease;
        await context.MediaItems.Where(m => m.Id == ids[0])
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.PlacementNextAttemptAt, leaseEnd), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ids[0];
    }

    private async Task FailAsync(long itemId, Exception exception)
    {
        var now = clock.GetUtcNow();
        var attempts = await context.MediaItems.Where(m => m.Id == itemId).Select(m => (int?)m.PlacementAttempts)
            .SingleOrDefaultAsync(CancellationToken.None) is { } stored ? stored + 1 : 0;
        if (attempts == 0)
        {
            return;
        }

        if (attempts >= 5)
        {
            logger.LogError(exception, "Placing media item {MediaItemId} failed {Attempts} times; its variants may still lie under the wrong prefix.", itemId, attempts);
        }
        else
        {
            logger.LogWarning(exception, "Placing media item {MediaItemId} failed (attempt {Attempts}); it is attempted again.", itemId, attempts);
        }

        var delay = TimeSpan.FromTicks(Math.Min(options.Value.RetryDelay.Ticks * attempts, MaxRetryDelay.Ticks));
        await context.MediaItems.Where(m => m.Id == itemId).ExecuteUpdateAsync(
            s => s.SetProperty(m => m.PlacementAttempts, attempts).SetProperty(m => m.PlacementNextAttemptAt, now + delay), CancellationToken.None);
    }

    private async Task PlaceAsync(long itemId, CancellationToken cancellationToken)
    {
        for (var pass = 0; pass < MaxPassesPerRun; pass++)
        {
            context.ChangeTracker.Clear();
            var item = await context.MediaItems.Include(m => m.Variants).SingleOrDefaultAsync(m => m.Id == itemId, cancellationToken);
            if (item is null)
            {
                return;
            }

            var toPublic = await ShouldBePublicAsync(itemId, cancellationToken);
            await PlaceItemAsync(item, toPublic, cancellationToken);
            if (await TryFinishAsync(itemId, toPublic, cancellationToken))
            {
                return;
            }
        }

        // The answer or the variants kept changing while the passes ran; the item stays pending and is attempted again.
        await FailAsync(itemId, new InvalidOperationException("The placement did not settle within one run."));
    }

    private async Task<bool> ShouldBePublicAsync(long itemId, CancellationToken cancellationToken) =>
        (await context.AnonymousVisibleMediaItemIdsAsync([itemId], clock.GetUtcNow(), cancellationToken)).Contains(itemId);

    private async Task<bool> TryFinishAsync(long itemId, bool placedPublic, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var item = await context.MediaItems.FromSqlInterpolated($"SELECT * FROM public.media_item WHERE id = {itemId} FOR UPDATE")
            .Include(m => m.Variants).SingleOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            return true;
        }

        if (await ShouldBePublicAsync(itemId, cancellationToken) != placedPublic
            || !await IsSettledAsync(item, placedPublic, cancellationToken))
        {
            return false;
        }

        item.PlacedPublic = placedPublic;
        item.PlacementPending = false;
        item.PlacementAttempts = 0;
        item.PlacementNextAttemptAt = null;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Whether every movable variant lies on the side the placement asks for and no object is left on the
    /// other side. Runs under the row lock of the item, so changes that commit later cannot slip past.
    /// </summary>
    private async Task<bool> IsSettledAsync(MediaItem item, bool toPublic, CancellationToken cancellationToken)
    {
        if (item.Kind == MediaKind.Video)
        {
            if (!HasVideoDirectory(item))
            {
                return true;
            }

            var opposite = OppositeDirectory(item, toPublic);
            if (item.Variants.Any(v => v.StorageKey.StartsWith(opposite, StringComparison.Ordinal)))
            {
                return false;
            }

            await foreach (var _ in store.ListKeysAsync(opposite, cancellationToken))
            {
                return false;
            }

            return true;
        }

        foreach (var variant in item.Variants.Where(IsMovable))
        {
            if (variant.StorageKey != TargetOf(variant.StorageKey, toPublic)
                || await store.ExistsAsync(TargetOf(variant.StorageKey, !toPublic), cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasVideoDirectory(MediaItem item) => item.StorageKey.StartsWith("private/video/", StringComparison.Ordinal);

    private static bool IsMovable(MediaVariant variant) =>
        MediaStorageKeys.IsPublicVariantKey(variant.StorageKey) || MediaStorageKeys.IsPrivateVariantKey(variant.StorageKey);

    private static string OppositeDirectory(MediaItem item, bool toPublic) =>
        MediaStorageKeys.BuildVideoDirectory(item.StorageKey, isPrivate: toPublic);

    private async Task PlaceItemAsync(MediaItem item, bool toPublic, CancellationToken cancellationToken)
    {
        if (item.Kind == MediaKind.Video)
        {
            await PlaceVideoAsync(item, toPublic, cancellationToken);
            return;
        }

        foreach (var variant in item.Variants.Where(IsMovable).ToList())
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
        if (!HasVideoDirectory(item) || item.Variants.All(v => v.Label is not (VideoStorage.HlsLabel or VideoStorage.PosterLabel)))
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
/// Places the media items of a request that changes who sees media within the configured time budget,
/// for a caller that wants the affected items in their new area before it answers. It places with a
/// context of its own, so the work of the caller stays untouched. When the budget is used up or a
/// placement fails, the items stay pending and the worker continues.
/// </summary>
public sealed class MediaPlacementTrigger(
    IServiceScopeFactory scopeFactory, IOptions<MediaPlacementOptions> options, ILogger<MediaPlacementTrigger> logger)
{
    /// <summary>
    /// Places the due items among the given ids within the budget.
    /// </summary>
    public async Task PlaceNowAsync(IReadOnlyCollection<long> mediaItemIds)
    {
        if (mediaItemIds.Count == 0 || options.Value.ImmediateBudget <= TimeSpan.Zero)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(options.Value.ImmediateBudget);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var placement = scope.ServiceProvider.GetRequiredService<MediaPlacementService>();
            while (await placement.RunOnceAsync(timeout.Token, mediaItemIds))
            {
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Placing {Count} media items continues in the background.", mediaItemIds.Count);
        }
    }
}

/// <summary>
/// Places media for the lifetime of the host, one media item after the other, and reconciles the
/// recorded placement with the visibility rule at the configured interval. When no item is due the loop
/// waits for the poll interval.
/// </summary>
public sealed class MediaPlacementHostedService(
    IServiceScopeFactory scopeFactory, IOptions<MediaPlacementOptions> options, TimeProvider clock, ILogger<MediaPlacementHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var nextReconcile = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var placed = false;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var placement = scope.ServiceProvider.GetRequiredService<MediaPlacementService>();
                placed = await placement.RunOnceAsync(stoppingToken);
                if (!placed && clock.GetUtcNow() >= nextReconcile)
                {
                    await placement.ReconcileAsync(stoppingToken);
                    nextReconcile = clock.GetUtcNow() + options.Value.ReconcileInterval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A media placement pass failed.");
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
