using Cichlids.Api.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Cichlids.Infrastructure.Video;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// Processes one claimed job of the transcoding queue: downloads the private original to a
/// temporary file, probes it, transcodes it to HLS with a poster frame, stores the files below the
/// video's HLS directory (public, or private for the video of a care entry) and records the
/// variants and the finished job with the processed event in one transaction. A failure queues the
/// job again or, when its attempts are used up or the video itself is unusable, marks it failed.
/// Files written for a failed attempt are removed.
/// </summary>
public sealed class VideoTranscodingService(
    CichlidsDbContext context,
    VideoJobQueue queue,
    IObjectStore store,
    VideoLimitPolicy policy,
    FfprobeClient probe,
    HlsTranscoder transcoder,
    StoredObjectDeleter deleter,
    TimeProvider clock,
    ILogger<VideoTranscodingService> logger)
{
    /// <summary>
    /// Claims and processes the next due job. Returns false when the queue had no due job.
    /// </summary>
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        var job = await queue.ClaimAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        var directory = Directory.CreateTempSubdirectory("video-job-");
        var written = new List<string>();
        try
        {
            await ProcessAsync(job, directory.FullName, written, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await deleter.DeleteAsync(written);
            var permanent = ex is VideoProbeException;
            var code = ex is VideoProbeException probeError ? probeError.Code : VideoProcessingErrors.ProcessingFailed;
            logger.LogWarning(ex, "Transcoding of media item {MediaItemId} failed (attempt {Attempt}).", job.MediaItemId, job.Attempts);
            await queue.FailAsync(job, code, permanent, CancellationToken.None);
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        return true;
    }

    private async Task ProcessAsync(VideoJob job, string workDirectory, List<string> written, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var item = await context.MediaItems.Include(m => m.Variants).SingleAsync(m => m.Id == job.MediaItemId, cancellationToken);
        var input = Path.Combine(workDirectory, "original.mp4");
        await using (var file = new FileStream(input, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            if (item.ByteSize is not > 0 || !await store.DownloadAsync(item.StorageKey, file, item.ByteSize.Value, cancellationToken))
            {
                throw new InvalidOperationException($"The original of media item {item.Id} is not available.");
            }
        }

        var info = await probe.ProbeAsync(input, cancellationToken);
        var limits = await policy.ResolveAsync(item.OwnerProfileId ?? 0, cancellationToken);
        var output = Path.Combine(workDirectory, "hls");
        var result = await transcoder.TranscodeAsync(input, info, output, limits.MaxDuration, cancellationToken);

        var prefix = MediaStorageKeys.BuildVideoDirectory(item.StorageKey, job.IsPrivate);
        foreach (var relative in result.Files)
        {
            await using var stream = File.OpenRead(Path.Combine(output, relative));
            await store.PutAsync(prefix + relative, stream, MediaStorageKeys.ResolveHlsContentType(relative), cancellationToken);
            written.Add(prefix + relative);
        }

        var top = result.Renditions[^1];
        var now = clock.GetUtcNow();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // The files were written for the placement chosen when the job was queued; a tank whose
        // profile level has changed since is queued for the placement of its media.
        await context.Tanks
            .Where(t => context.TankMedia.Any(m => m.TankId == t.Id && m.MediaItemId == item.Id)
                && (t.ProfileVisibility == TankVisibility.Public) == job.IsPrivate)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MediaMovePending, true).SetProperty(t => t.MediaMoveAttempts, 0)
                .SetProperty(t => t.MediaMoveNextAttemptAt, now), cancellationToken);
        item.Width = info.DisplayWidth;
        item.Height = info.DisplayHeight;
        item.DurationMs = (int)info.Duration.TotalMilliseconds;
        item.Variants.RemoveAll(v => v.Label is VideoStorage.HlsLabel or VideoStorage.PosterLabel);
        item.Variants.Add(new MediaVariant { Label = VideoStorage.HlsLabel, Width = top.Width, StorageKey = prefix + HlsTranscoder.MasterPlaylistName });
        item.Variants.Add(new MediaVariant { Label = VideoStorage.PosterLabel, Width = top.Width, StorageKey = prefix + HlsTranscoder.PosterName });
        var stored = await context.VideoJobs.SingleAsync(j => j.Id == job.Id, cancellationToken);
        stored.Status = VideoJobStatus.Done;
        stored.LockedUntil = null;
        stored.ErrorCode = null;
        stored.FinishedAt = now;
        VideoEvents.Add(context, VideoEvents.Processed, item.Id, item.OwnerProfileId, now,
            new { durationMs = item.DurationMs, renditions = result.Renditions.Select(r => r.Label) });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

/// <summary>
/// Processes the transcoding queue for the lifetime of the host, one video after the other. When
/// the queue is empty the loop waits for the poll interval.
/// </summary>
public sealed class VideoTranscodingHostedService(
    IServiceScopeFactory scopeFactory, Microsoft.Extensions.Options.IOptions<VideoProcessingOptions> options, ILogger<VideoTranscodingHostedService> logger)
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
            var processed = false;
            try
            {
                using var scope = scopeFactory.CreateScope();
                processed = await scope.ServiceProvider.GetRequiredService<VideoTranscodingService>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A video transcoding pass failed.");
            }

            if (processed)
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
