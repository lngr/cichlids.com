using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// One cleanup pass over abandoned drafts: every unpublished draft last changed longer ago than the
/// retention is deleted with its links; its photos stay in the media library. Several instances may run a pass at the
/// same time; the delete of a draft happens once and a draft another instance removed first is
/// skipped.
/// </summary>
public sealed class DraftCleanupService(
    CichlidsDbContext context,
    UploadsWriteService uploadsWriteService,
    IOptions<DraftCleanupOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Deletes up to one batch of expired drafts and returns how many this pass deleted.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = timeProvider.GetUtcNow() - settings.Retention;

        var expiredIds = await context.Stories
            .Where(p => p.State == StoryState.Draft && p.DeletedAt == null && p.UpdatedAt < cutoff)
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        var deleted = 0;
        foreach (var storyId in expiredIds)
        {
            if (await uploadsWriteService.DeleteExpiredDraftAsync(storyId, cutoff, cancellationToken))
            {
                deleted++;
            }
        }

        return deleted;
    }
}
