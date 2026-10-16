// Story: TASK-5.20.5
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class QuickShareMigrationTests(PostgresFixture fixture)
{
    private const string Seed = """
        INSERT INTO public.profile (id, username, kind, created_at) VALUES (1, 'owner', 'member', now());
        INSERT INTO public.media_item (id, owner_profile_id, kind, storage_key, created_at) VALUES (10, 1, 'photo', 'k10', now()), (11, 1, 'photo', 'k11', now());
        INSERT INTO public.story (id, author_profile_id, topic, state, created_at, updated_at) VALUES
            (1, 1, 'cichlids', 'draft', now(), now()), (2, 1, 'cichlids', 'draft', now(), now());
        """;

    [Fact]
    public async Task GivenAStoryThatIsTheQuickShareOfAMediaItem_ThenNoSecondItemTakesTheSameStory()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(Seed);
        await context.Database.ExecuteSqlRawAsync("UPDATE public.media_item SET quick_share_story_id = 1 WHERE id = 10");

        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE public.media_item SET quick_share_story_id = 1 WHERE id = 11"));
        await context.Database.ExecuteSqlRawAsync("UPDATE public.media_item SET quick_share_story_id = 2 WHERE id = 11");
    }

    [Fact]
    public async Task GivenAQuickShareStory_WhenTheStoryIsDeleted_ThenTheMarkerOfTheMediaItemIsCleared()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(Seed);
        await context.Database.ExecuteSqlRawAsync("UPDATE public.media_item SET quick_share_story_id = 1 WHERE id = 10");

        await context.Database.ExecuteSqlRawAsync("DELETE FROM public.story WHERE id = 1");

        Assert.Null(await context.MediaItems.Where(m => m.Id == 10).Select(m => m.QuickShareStoryId).SingleAsync());
    }
}
