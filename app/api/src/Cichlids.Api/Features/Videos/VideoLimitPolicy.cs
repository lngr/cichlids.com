using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// The single place that decides how long a video of a member may be. A member whose account is
/// older than the configured age and who has more than the configured number of published stories
/// may upload the long maximum; every other member gets the short maximum. The size limit follows
/// from the length. The provisional profile of an app installation has a shorter length, fewer open
/// uploads and a cap on the videos it holds.
/// </summary>
public sealed class VideoLimitPolicy(CichlidsDbContext context, IOptions<VideoLimitOptions> options, TimeProvider timeProvider)
{
    public async Task<VideoLimits> ResolveAsync(long profileId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (await context.Profiles.AsNoTracking().AnyAsync(p => p.Id == profileId && p.Kind == ProfileKind.Provisional, cancellationToken))
        {
            return VideoLimits.For(
                settings.ProvisionalMaxDuration, settings.MaxBitrate, settings.ProvisionalMaxOpenUploads, settings.ProvisionalMaxVideos);
        }

        var createdAt = await context.Profiles.AsNoTracking()
            .Where(p => p.Id == profileId).Select(p => (DateTimeOffset?)p.CreatedAt).SingleOrDefaultAsync(cancellationToken);

        var oldEnough = createdAt is { } created && timeProvider.GetUtcNow() - created > settings.MinAccountAge;
        var allowsLong = oldEnough && await HasMoreThanAsync(profileId, settings.MinPublishedStories, cancellationToken);
        return VideoLimits.For(allowsLong ? settings.MaxDuration : settings.ShortMaxDuration, settings.MaxBitrate, settings.MaxOpenUploads);
    }

    private async Task<bool> HasMoreThanAsync(long profileId, int minimum, CancellationToken cancellationToken)
    {
        var count = await context.Stories.AsNoTracking().CountAsync(
            p => p.AuthorProfileId == profileId && p.State == StoryState.Published && p.DeletedAt == null,
            cancellationToken);
        return count > minimum;
    }
}
