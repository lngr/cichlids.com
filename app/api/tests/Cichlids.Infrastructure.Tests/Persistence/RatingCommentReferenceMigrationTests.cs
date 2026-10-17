using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class RatingCommentReferenceMigrationTests(PostgresFixture fixture)
{
    private const string Seed = """
        INSERT INTO public.profile (id, username, kind, created_at) VALUES
            (1, 'owner', 'member', now()), (2, 'rater', 'member', now()), (3, 'other', 'member', now());
        INSERT INTO public.story (id, author_profile_id, topic, state, created_at, updated_at) VALUES
            (1, 1, 'cichlids', 'published', now(), now());
        INSERT INTO public.tank (id, profile_id, title, state, created_at) VALUES (1, 1, 'Tank', 'published', now());

        INSERT INTO public.comment (id, story_id, author_profile_id, body, created_at) VALUES
            (10, 1, 2, 'api story', '2026-10-01 10:00:00+00'),
            (11, 1, 3, 'earlier comment of a replaced rating', '2026-10-01 09:00:00+00'),
            (12, 1, 3, 'newest comment of the replaced rating', '2026-10-02 09:00:00+00');
        INSERT INTO public.rating (id, story_id, profile_id, stars, created_at) VALUES
            (20, 1, 2, 4, '2026-10-01 10:00:00+00'),
            (21, 1, 3, 5, '2026-10-02 09:00:00+00');

        INSERT INTO public.comment (id, tank_id, author_profile_id, body, created_at) VALUES
            (13, 1, 2, 'api tank', '2026-10-03 10:00:00+00');
        INSERT INTO public.rating (id, tank_id, profile_id, stars, created_at) VALUES
            (22, 1, 2, 3, '2026-10-03 10:00:00+00'),
            (23, 1, 3, 2, '2026-10-04 10:00:00+00');

        INSERT INTO public.comment (id, legacy_id, story_id, poster_name, body, created_at) VALUES
            (14, 777, 1, 'Guest', 'legacy', '2020-01-01 10:00:00+00'),
            (15, 778, 1, 'Guest', 'legacy without rating', '2020-01-02 10:00:00+00');
        INSERT INTO public.rating (id, legacy_comment_id, story_id, stars, created_at) VALUES
            (24, 777, 1, 2, '2020-01-01 12:00:00+00'),
            (25, 779, 1, 1, '2020-01-03 10:00:00+00');
        """;

    [Fact]
    public async Task GivenRatingsAndCommentsOfTheEarlierSchema_WhenMigrated_ThenEveryPairReferencesItsComment()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrations = context.Database.GetMigrations().ToList();
        var target = migrations.FindIndex(m => m.EndsWith("_RatingCommentReference", StringComparison.Ordinal));
        Assert.True(target > 0);

        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[target - 1]);
        await context.Database.ExecuteSqlRawAsync(Seed);

        await migrator.MigrateAsync();

        var references = await context.Ratings.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.CommentId);
        Assert.Equal(10, references[20]);
        Assert.Equal(12, references[21]);
        Assert.Equal(13, references[22]);
        Assert.Null(references[23]);
        Assert.Equal(14, references[24]);
        Assert.Null(references[25]);
    }

    [Fact]
    public async Task GivenARatingWithACommentReference_WhenTheCommentIsDeleted_ThenTheRatingStaysWithoutReference()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(Seed);
        await context.Database.ExecuteSqlRawAsync("UPDATE public.rating SET comment_id = 10 WHERE id = 20");

        await context.Database.ExecuteSqlRawAsync("DELETE FROM public.comment WHERE id = 10");

        Assert.Null(await context.Ratings.Where(r => r.Id == 20).Select(r => r.CommentId).SingleAsync());
    }

    [Fact]
    public async Task GivenACommentWithARating_WhenASecondRatingReferencesIt_ThenTheDatabaseRefuses()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(Seed);
        await context.Database.ExecuteSqlRawAsync("UPDATE public.rating SET comment_id = 10 WHERE id = 20");

        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE public.rating SET comment_id = 10 WHERE id = 21"));
    }
}
