using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// Removes upload sessions that were not completed before they expired, together with the parts
/// already stored for them.
/// </summary>
public sealed class VideoUploadCleanupService(
    CichlidsDbContext context, IObjectStore store, TimeProvider clock, ILogger<VideoUploadCleanupService> logger)
{
    public const int BatchSize = 100;

    /// <summary>
    /// Removes one batch of expired sessions and returns how many it removed.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var expired = await context.VideoUploads.AsNoTracking().Where(u => u.ExpiresAt <= now)
            .OrderBy(u => u.ExpiresAt).Take(BatchSize).ToListAsync(cancellationToken);
        var removed = 0;
        foreach (var upload in expired)
        {
            // Deleting the row claims the session against a concurrent completion or cleanup.
            if (await context.VideoUploads.Where(u => u.Id == upload.Id && u.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken) == 0)
            {
                continue;
            }

            removed++;
            try
            {
                await store.AbortMultipartUploadAsync(upload.StorageKey, upload.MultipartUploadId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not abort the multipart upload of {StorageKey}.", upload.StorageKey);
            }
        }

        return removed;
    }
}

/// <summary>
/// Runs the cleanup of abandoned video uploads for the lifetime of the host.
/// </summary>
public sealed class VideoUploadCleanupHostedService(
    IServiceScopeFactory scopeFactory, Microsoft.Extensions.Options.IOptions<VideoLimitOptions> options, ILogger<VideoUploadCleanupHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.CleanupEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var removed = await scope.ServiceProvider.GetRequiredService<VideoUploadCleanupService>().RunOnceAsync(stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("Removed {Count} abandoned video upload(s).", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cleanup of abandoned video uploads failed.");
            }

            try
            {
                await Task.Delay(options.Value.CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
