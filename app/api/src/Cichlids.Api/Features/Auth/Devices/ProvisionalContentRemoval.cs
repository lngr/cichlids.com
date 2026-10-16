using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// Deletes a provisional profile together with everything that references it, in the order the
/// restricting foreign keys need: comments, ratings, follows, posts, collections and stories first,
/// then to-dos, care entries, plan rules, tanks and media items, and the profile last. Rows that
/// cascade (streak data, snoozes, sessions, checksums, credentials) follow with the tanks and the
/// profile. The caller holds the transaction and removes the stored objects of the media items.
/// </summary>
internal static class ProvisionalContentRemoval
{
    public static async Task DeleteAsync(CichlidsDbContext context, long profileId, CancellationToken cancellationToken)
    {
        var tanks = context.Tanks.Where(t => t.ProfileId == profileId).Select(t => t.Id);
        var stories = context.Stories.Where(s => s.AuthorProfileId == profileId).Select(s => s.Id);

        await context.Comments.Where(c => c.AuthorProfileId == profileId || (c.TankId != null && tanks.Contains(c.TankId.Value))
            || (c.StoryId != null && stories.Contains(c.StoryId.Value))).ExecuteDeleteAsync(cancellationToken);
        await context.Ratings.Where(r => r.ProfileId == profileId || (r.TankId != null && tanks.Contains(r.TankId.Value))
            || (r.StoryId != null && stories.Contains(r.StoryId.Value))).ExecuteDeleteAsync(cancellationToken);
        await context.Follows.Where(f => f.FollowerProfileId == profileId || f.TargetProfileId == profileId
            || (f.TargetTankId != null && tanks.Contains(f.TargetTankId.Value))).ExecuteDeleteAsync(cancellationToken);
        await context.DiscussionPosts.Where(p => p.AuthorProfileId == profileId).ExecuteDeleteAsync(cancellationToken);
        await context.Collections.Where(c => c.ProfileId == profileId).ExecuteDeleteAsync(cancellationToken);
        await context.SlugAliases.Where(a => stories.Contains(a.StoryId)).ExecuteDeleteAsync(cancellationToken);
        await context.Stories.Where(s => s.AuthorProfileId == profileId).ExecuteDeleteAsync(cancellationToken);

        await context.CareTodos.Where(t => t.ProfileId == profileId || tanks.Contains(t.TankId)).ExecuteDeleteAsync(cancellationToken);
        await context.CareEntries.Where(e => e.AuthorProfileId == profileId || tanks.Contains(e.TankId)).ExecuteDeleteAsync(cancellationToken);
        await context.CarePlanRules.Where(r => tanks.Contains(r.TankId)).ExecuteDeleteAsync(cancellationToken);
        await context.Tanks.Where(t => t.ProfileId == profileId).ExecuteDeleteAsync(cancellationToken);

        await context.MediaItems.Where(m => m.OwnerProfileId == profileId).ExecuteDeleteAsync(cancellationToken);
        await context.Profiles.Where(p => p.Id == profileId).ExecuteDeleteAsync(cancellationToken);
    }
}
