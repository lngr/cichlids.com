using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// The durable queue of video transcoding jobs. A worker claims one job at a time: the claim
/// locks the oldest due row with SKIP LOCKED, so any number of workers on any number of API
/// instances never receive the same job. A claimed job is leased; when the worker dies, the job is
/// claimed again after the lease, until its attempts are used up and it is marked failed. Every
/// state change that ends a job commits together with its outbox event.
/// </summary>
public sealed class VideoJobQueue(CichlidsDbContext context, IOptions<VideoProcessingOptions> options, TimeProvider timeProvider)
{
    /// <summary>
    /// Claims the next due job for this worker, or returns null when none is due.
    /// </summary>
    public async Task<VideoJob?> ClaimAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        while (true)
        {
            var now = timeProvider.GetUtcNow();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var id = await context.Database.SqlQuery<long>($"""
                SELECT id AS "Value" FROM public.video_job
                WHERE (status = 'queued' AND run_after <= {now}) OR (status = 'running' AND locked_until < {now})
                ORDER BY run_after, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """).ToListAsync(cancellationToken);
            if (id.Count == 0)
            {
                return null;
            }

            var job = await context.VideoJobs.SingleAsync(j => j.Id == id[0], cancellationToken);
            if (job.Status == VideoJobStatus.Running && job.Attempts >= settings.MaxAttempts)
            {
                await MarkFailedAsync(job, VideoProcessingErrors.ProcessingFailed, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                context.ChangeTracker.Clear();
                continue;
            }

            job.Status = VideoJobStatus.Running;
            job.Attempts++;
            job.LockedUntil = now + settings.Lease;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return job;
        }
    }

    /// <summary>
    /// Records a failed attempt of a claimed job. A permanent failure, or a failure of the last
    /// attempt, marks the job failed with the given code and raises the failed event; any other
    /// failure queues the job again after a delay that grows with the attempts. Returns the new
    /// status.
    /// </summary>
    public async Task<VideoJobStatus> FailAsync(VideoJob claimed, string errorCode, bool permanent, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.ChangeTracker.Clear();
        var job = await context.VideoJobs.SingleAsync(j => j.Id == claimed.Id, cancellationToken);
        if (permanent || job.Attempts >= settings.MaxAttempts)
        {
            await MarkFailedAsync(job, errorCode, now, cancellationToken);
        }
        else
        {
            job.Status = VideoJobStatus.Queued;
            job.RunAfter = now + (settings.RetryDelay * job.Attempts);
            job.LockedUntil = null;
            job.ErrorCode = errorCode;
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return job.Status;
    }

    private async Task MarkFailedAsync(VideoJob job, string errorCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        job.Status = VideoJobStatus.Failed;
        job.ErrorCode = errorCode;
        job.LockedUntil = null;
        job.FinishedAt = now;
        var owner = await context.MediaItems.AsNoTracking()
            .Where(m => m.Id == job.MediaItemId).Select(m => m.OwnerProfileId).SingleOrDefaultAsync(cancellationToken);
        VideoEvents.Add(context, VideoEvents.Failed, job.MediaItemId, owner, now, new { code = errorCode });
        await context.SaveChangesAsync(cancellationToken);
    }
}
