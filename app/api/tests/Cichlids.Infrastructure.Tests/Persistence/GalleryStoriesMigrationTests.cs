using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class GalleryStoriesMigrationTests(PostgresFixture fixture)
{
    private const string PreviousMigration = "20261016164952_MediaVisibilityTriggers";

    private const string Seed = """
        INSERT INTO public.profile (id, username, kind, created_at) VALUES (1, 'owner', 'member', now());
        INSERT INTO public.media_item (id, owner_profile_id, kind, storage_key, created_at) VALUES
            (10, 1, 'photo', 'k10', now()), (11, 1, 'photo', 'k11', now());
        INSERT INTO public.tank (id, profile_id, title, state, created_at) VALUES (1, 1, 'Tank', 'published', now());
        INSERT INTO public.species (id, genus, name, display_name, breeding, aggression, intra_aggression, diet, created_at) VALUES
            (1, 'Tropheus', 'duboisi', 'Tropheus duboisi', 'unspecified', 'unspecified', 'unspecified', 'unspecified', now()),
            (2, 'Julidochromis', 'regani', 'Julidochromis regani', 'unspecified', 'unspecified', 'unspecified', 'unspecified', now());
        INSERT INTO public.post (id, author_profile_id, tank_id, kind, topic, state, title, created_at, updated_at, published_at)
            OVERRIDING SYSTEM VALUE VALUES
            (5, 1, 1, 'story', 'tanks', 'published', 'Two photos', now(), now(), now()),
            (6, 1, NULL, 'single', 'cichlids', 'draft', 'One photo', now(), now(), NULL);
        INSERT INTO public.post_species (post_id, species_id, sort) VALUES (5, 2, 0), (5, 1, 1);
        INSERT INTO public.media_link (media_item_id, context_type, role, position, post_id) VALUES
            (10, 'post', 'item', 0, 5), (11, 'post', 'item', 1, 5), (10, 'post', 'item', 0, 6);
        INSERT INTO public.outbox_event (id, occurred_at, event_type, aggregate_type, aggregate_id, payload, next_attempt_at)
            VALUES ('019a0000-0000-7000-8000-000000000001', now(), 'post.published', 'post', '5', '{"postId": 5, "slug": "a"}', now()),
                   ('019a0000-0000-7000-8000-000000000002', now(), 'comment.created', 'tank', '1', '{"postId": null, "tankId": 1}', now());
        INSERT INTO public.webhook_subscription (url, secret, event_types, active, created_at)
            VALUES ('https://example.test/hook', 's', ARRAY['post.published', 'tank.created'], true, now());
        """;

    [Fact]
    public async Task GalleryEntriesBecomeStoriesWithTheirItemsAndSubjects()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync(Seed.Replace("{", "{{").Replace("}", "}}"));

        await migrator.MigrateAsync();

        var stories = await context.Stories.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        Assert.Equal([5L, 6L], stories.Select(s => s.Id));
        Assert.Equal([StoryState.Published, StoryState.Draft], stories.Select(s => s.State));
        Assert.All(stories, s => Assert.Null(s.CoverMediaLinkId));

        var links = await context.MediaLinks.AsNoTracking().Where(l => l.StoryId != null).OrderBy(l => l.StoryId).ThenBy(l => l.Position).ToListAsync();
        Assert.All(links, l => Assert.Equal(MediaLinkContext.Story, l.ContextType));
        Assert.Equal([(5L, 10L), (5L, 11L), (6L, 10L)], links.Select(l => (l.StoryId!.Value, l.MediaItemId)));

        var subjects = await context.StorySubjects.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(
            [(StorySubjectType.Tank, 5L, 1L), (StorySubjectType.Species, 5L, 2L), (StorySubjectType.Species, 5L, 1L)],
            subjects.Select(s => (s.SubjectType, s.StoryId, s.TankId ?? s.SpeciesId!.Value)));
        Assert.All(subjects, s => Assert.Null(s.Position));

        Assert.Equal(7L, await NextStoryIdAsync(context));
    }

    [Fact]
    public async Task OutboxEventsAndWebhookSubscriptionsNameTheStory()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync(Seed.Replace("{", "{{").Replace("}", "}}"));

        await migrator.MigrateAsync();

        var published = await context.OutboxEvents.AsNoTracking().SingleAsync(e => e.AggregateId == "5");
        Assert.Equal("story.published", published.EventType);
        Assert.Equal("story", published.AggregateType);
        Assert.Contains("\"storyId\": 5", published.Payload);
        Assert.DoesNotContain("postId", published.Payload);

        var comment = await context.OutboxEvents.AsNoTracking().SingleAsync(e => e.EventType == "comment.created");
        Assert.Equal("tank", comment.AggregateType);
        Assert.Contains("storyId", comment.Payload);

        var subscription = await context.WebhookSubscriptions.AsNoTracking().SingleAsync();
        Assert.Equal(["story.published", "tank.created"], subscription.EventTypes);
    }

    [Fact]
    public async Task PublishingAStoryStillChangesTheVisibilityOfItsMedia()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync(Seed.Replace("{", "{{").Replace("}", "}}"));
        await migrator.MigrateAsync();
        var before = await context.Database.SqlQuery<long>($"SELECT visibility_generation AS \"Value\" FROM public.media_item WHERE id = 11").SingleAsync();

        await context.Database.ExecuteSqlRawAsync("UPDATE public.story SET state = 'archived' WHERE id = 5");

        var after = await context.Database.SqlQuery<long>($"SELECT visibility_generation AS \"Value\" FROM public.media_item WHERE id = 11").SingleAsync();
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task DownRestoresPostsWithTheirTankSpeciesAndKind()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync(Seed.Replace("{", "{{").Replace("}", "}}"));
        await migrator.MigrateAsync();

        await migrator.MigrateAsync(PreviousMigration);

        var posts = await context.Database.SqlQuery<PostRow>($"SELECT id, tank_id, kind FROM public.post ORDER BY id").ToListAsync();
        Assert.Equal([new PostRow(5, 1, "story"), new PostRow(6, null, "single")], posts);
        var species = await context.Database.SqlQuery<long>($"SELECT species_id AS \"Value\" FROM public.post_species WHERE post_id = 5 ORDER BY sort").ToListAsync();
        Assert.Equal([2L, 1L], species);
        Assert.Equal(3, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.media_link WHERE context_type = 'post'").SingleAsync());
        Assert.Equal("post.published", await context.Database.SqlQuery<string>($"SELECT event_type AS \"Value\" FROM public.outbox_event WHERE aggregate_id = '5'").SingleAsync());
    }

    private static Task<long> NextStoryIdAsync(Cichlids.Infrastructure.Persistence.CichlidsDbContext context) =>
        context.Database.SqlQuery<long>($"SELECT nextval(pg_get_serial_sequence('public.story', 'id')) AS \"Value\"").SingleAsync();

    private sealed record PostRow(long Id, long? TankId, string Kind);
}
