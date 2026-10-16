using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// The single place that decides how long a video of a member may be. A member whose account is
/// older than the configured age and who has more than the configured number of public pictures
/// may upload the long maximum; every other member gets the short maximum. The size limit follows
/// from the length.
/// </summary>
public sealed class VideoLimitPolicy(CichlidsDbContext context, IOptions<VideoLimitOptions> options, TimeProvider timeProvider)
{
    public async Task<VideoLimits> ResolveAsync(long profileId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var createdAt = await context.Profiles.AsNoTracking()
            .Where(p => p.Id == profileId).Select(p => (DateTimeOffset?)p.CreatedAt).SingleOrDefaultAsync(cancellationToken);

        var oldEnough = createdAt is { } created && timeProvider.GetUtcNow() - created > settings.MinAccountAge;
        var allowsLong = oldEnough && await HasMoreThanAsync(profileId, settings.MinPublicPictures, cancellationToken);
        return VideoLimits.For(allowsLong ? settings.MaxDuration : settings.ShortMaxDuration, settings.MaxBitrate);
    }

    private async Task<bool> HasMoreThanAsync(long profileId, int minimum, CancellationToken cancellationToken)
    {
        var count = await context.Posts.AsNoTracking().CountAsync(
            p => p.AuthorProfileId == profileId && p.Kind == PostKind.Single && p.State == PostState.Published && p.DeletedAt == null,
            cancellationToken);
        return count > minimum;
    }
}
