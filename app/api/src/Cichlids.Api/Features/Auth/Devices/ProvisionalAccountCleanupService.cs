using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// One cleanup pass over provisional accounts: every provisional profile whose most recent signed
/// request of any credential (its creation time without a credential) is older than the configured
/// number of calendar months is removed with all its content in one transaction. The stored objects
/// of its media are deleted after the commit, so a failing object delete leaves unreferenced objects
/// behind (logged) and never rows that point at missing objects. The profile row and its credentials
/// are locked and the inactivity is checked again under the lock, so an installation that signs in
/// at the same moment keeps its profile, and a profile another instance holds is skipped. Removing
/// an account emits no outbox events. Registered members are never candidates.
/// </summary>
public sealed class ProvisionalAccountCleanupService(
    CichlidsDbContext context,
    IOptions<ProvisionalAccountCleanupOptions> options,
    StoredObjectDeleter deleter,
    VideoRemovalService videoRemoval,
    IObjectStore store,
    TimeProvider clock,
    ILogger<ProvisionalAccountCleanupService> logger)
{
    /// <summary>
    /// Removes the abandoned provisional accounts and returns how many this pass removed. An account
    /// whose removal fails is logged and left in place; the pass goes on with the next one.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = clock.GetUtcNow().AddMonths(-settings.InactivityMonths);

        var removed = 0;
        long lastId = 0;
        while (true)
        {
            var candidates = await context.Profiles.AsNoTracking()
                .Where(p => p.Id > lastId && p.Kind == ProfileKind.Provisional
                    && (context.DeviceCredentials.Where(c => c.ProfileId == p.Id).Max(c => (DateTimeOffset?)c.LastSignedRequestAt) ?? p.CreatedAt) < cutoff)
                .OrderBy(p => p.Id)
                .Select(p => p.Id)
                .Take(settings.BatchSize)
                .ToListAsync(cancellationToken);
            if (candidates.Count == 0)
            {
                return removed;
            }

            foreach (var profileId in candidates)
            {
                try
                {
                    if (await RemoveAsync(profileId, cutoff, cancellationToken))
                    {
                        removed++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    context.ChangeTracker.Clear();
                    logger.LogError(ex, "Could not remove the abandoned provisional profile {ProfileId}.", profileId);
                }
            }

            lastId = candidates[^1];
        }
    }

    private async Task<bool> RemoveAsync(long profileId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        List<MediaItem> media;
        List<VideoUpload> uploads;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            var locked = await context.Database
                .SqlQuery<long>($"SELECT id AS \"Value\" FROM public.profile WHERE id = {profileId} AND kind = 'provisional' FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);
            if (locked.Count == 0)
            {
                return false;
            }

            // A concurrent exchange of a signed challenge updates its credential row; holding the rows
            // makes that update wait for this transaction and then find no credential.
            await context.Database
                .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM public.device_credential WHERE profile_id = {profileId} FOR UPDATE")
                .ToListAsync(cancellationToken);
            var stillInactive = await context.Profiles.AsNoTracking()
                .AnyAsync(p => p.Id == profileId && p.Kind == ProfileKind.Provisional
                    && (context.DeviceCredentials.Where(c => c.ProfileId == p.Id).Max(c => (DateTimeOffset?)c.LastSignedRequestAt) ?? p.CreatedAt) < cutoff,
                    cancellationToken);
            if (!stillInactive)
            {
                return false;
            }

            media = await context.MediaItems.AsNoTracking().Include(m => m.Variants)
                .Where(m => m.OwnerProfileId == profileId).ToListAsync(cancellationToken);
            uploads = await context.VideoUploads.AsNoTracking().Where(u => u.OwnerProfileId == profileId).ToListAsync(cancellationToken);
            await ProvisionalContentRemoval.DeleteAsync(context, profileId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        try
        {
            await DeleteObjectsAsync(media, uploads);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not delete all stored objects of a removed provisional profile.");
        }

        logger.LogInformation("Removed an abandoned provisional profile with {MediaCount} media file(s).", media.Count);
        return true;
    }

    private async Task DeleteObjectsAsync(List<MediaItem> media, List<VideoUpload> uploads)
    {
        await videoRemoval.DeleteObjectsAsync(media.Where(m => m.Kind == MediaKind.Video));
        await deleter.DeleteAsync(media.Where(m => m.Kind != MediaKind.Video).SelectMany(m => m.Variants.Select(v => v.StorageKey).Append(m.StorageKey)));
        foreach (var upload in uploads)
        {
            try
            {
                await store.AbortMultipartUploadAsync(upload.StorageKey, upload.MultipartUploadId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not abort the multipart upload of {StorageKey}.", upload.StorageKey);
            }
        }
    }
}
