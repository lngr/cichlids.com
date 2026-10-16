using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Stories;

/// <summary>
/// Row access to the stories of their author.
/// </summary>
internal static class StoryRows
{
    /// <summary>
    /// Locks the story row of the author for the rest of the open transaction and returns its state, or null
    /// when the author has no such story that is neither deleted nor archived. The lock serializes edits and
    /// state changes of one story.
    /// </summary>
    public static async Task<StoryState?> LockOwnStoryAsync(
        this CichlidsDbContext context, long storyId, long authorProfileId, CancellationToken cancellationToken) =>
        await context.Stories
            .FromSqlInterpolated($"""
                SELECT * FROM public.story
                WHERE id = {storyId} AND author_profile_id = {authorProfileId} AND deleted_at IS NULL AND state <> 'archived'
                FOR UPDATE
                """)
            .Select(s => (StoryState?)s.State).SingleOrDefaultAsync(cancellationToken);
}
