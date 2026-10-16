using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class MediaLinkMigrationTests(PostgresFixture fixture)
{
    private const string PreviousMigration = "20261016144640_TankDecommissioning";

    // One attachment in every context, with the provenance the old tables kept on the attachment.
    private const string SeedAttachments = """
        INSERT INTO public.profile (id, username, kind, created_at) VALUES (1, 'owner', 'member', now());
        INSERT INTO public.media_item (id, owner_profile_id, kind, storage_key, created_at) VALUES
            (10, 1, 'photo', 'k10', '2026-01-01T10:00:00Z'), (11, 1, 'photo', 'k11', '2026-01-01T11:00:00Z'),
            (12, 1, 'photo', 'k12', '2026-01-01T12:00:00Z'), (13, 1, 'photo', 'k13', '2026-01-01T13:00:00Z'),
            (14, 1, 'photo', 'k14', '2026-01-01T14:00:00Z'), (15, 1, 'photo', 'k15', '2026-01-01T15:00:00Z'),
            (16, 1, 'video', 'k16', '2026-01-01T16:00:00Z'), (17, 1, 'video', 'k17', '2026-01-01T17:00:00Z'),
            (18, NULL, 'photo', 'k18', '2026-01-01T18:00:00Z'), (19, 1, 'photo', 'k19', '2026-01-01T19:00:00Z'),
            (20, 1, 'photo', 'k20', '2026-01-01T20:00:00Z'), (21, 1, 'photo', 'k21', '2026-01-01T21:00:00Z');
        INSERT INTO public.tank (id, profile_id, title, state, created_at, main_media_id) VALUES (1, 1, 'Tank', 'published', now(), 10);
        INSERT INTO public.tank_media (id, tank_id, media_item_id, section, sort, captured_in_app) VALUES
            (1, 1, 10, 'showcase', 0, false), (2, 1, 11, 'showcase', 1, false), (3, 1, 12, 'technic', 0, false),
            (4, 1, 13, 'time_lapse', 0, true), (5, 1, 16, 'video', 0, false), (6, 1, 14, 'decoration', 0, false),
            (7, 1, 12, 'decommission', 0, true), (8, 1, 13, 'recommission', 0, true);
        INSERT INTO public.post (id, author_profile_id, kind, topic, state, created_at, updated_at) VALUES (1, 1, 'single', 'cichlids', 'published', now(), now());
        INSERT INTO public.post_media (id, post_id, media_item_id, sort) VALUES (1, 1, 14, 0), (2, 1, 15, 1);
        INSERT INTO public.care_entry (id, tank_id, author_profile_id, kind, occurred_at, recorded_at) VALUES (1, 1, 1, 'water_change', now(), now());
        INSERT INTO public.care_photo (id, care_entry_id, media_item_id, position) VALUES (1, 1, 20, 0), (2, 1, 21, 1);
        INSERT INTO public.care_video (id, care_entry_id, media_item_id) VALUES (1, 1, 17), (2, 1, 16);
        INSERT INTO public.water_change_session (id, tank_id, profile_id, started_at) VALUES (1, 1, 1, '2026-01-02T10:00:00Z');
        INSERT INTO public.water_change_session_photo (id, session_id, media_item_id, position, captured_in_app, received_at)
            VALUES (1, 1, 19, 0, true, '2026-01-02T10:05:00Z');
        INSERT INTO public.discussion_thread (id, category, title, state, created_at) VALUES (1, 'cichlids', 'Thread', 'open', now());
        INSERT INTO public.discussion_post (id, thread_id, body, created_at) VALUES (1, 1, 'Body', now());
        INSERT INTO public.discussion_post_media (id, discussion_post_id, media_item_id, sort) VALUES (1, 1, 18, 0);
        UPDATE public.profile SET avatar_media_id = 11, profile_image_media_id = 12 WHERE id = 1;
        """;

    [Fact]
    public async Task EveryAttachmentOfEveryContextBecomesALinkWithItsRoleAndPosition()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync(SeedAttachments);

        await migrator.MigrateAsync();

        var links = await context.MediaLinks.AsNoTracking().ToListAsync();
        (MediaLinkContext Context, long Key, MediaLinkRole Role, int Position, long Item)[] expected =
        [
            (MediaLinkContext.Tank, 1, MediaLinkRole.Main, 0, 10),
            (MediaLinkContext.Tank, 1, MediaLinkRole.Showcase, 0, 10),
            (MediaLinkContext.Tank, 1, MediaLinkRole.Showcase, 1, 11),
            (MediaLinkContext.Tank, 1, MediaLinkRole.Technic, 0, 12),
            (MediaLinkContext.Tank, 1, MediaLinkRole.TimeLapse, 0, 13),
            (MediaLinkContext.Tank, 1, MediaLinkRole.Video, 0, 16),
            (MediaLinkContext.Tank, 1, MediaLinkRole.Decoration, 0, 14),
            (MediaLinkContext.Tank, 1, MediaLinkRole.Decommission, 0, 12),
            (MediaLinkContext.Tank, 1, MediaLinkRole.Recommission, 0, 13),
            (MediaLinkContext.Story, 1, MediaLinkRole.Item, 0, 14),
            (MediaLinkContext.Story, 1, MediaLinkRole.Item, 1, 15),
            (MediaLinkContext.CareEntry, 1, MediaLinkRole.Photo, 0, 20),
            (MediaLinkContext.CareEntry, 1, MediaLinkRole.Photo, 1, 21),
            (MediaLinkContext.CareEntry, 1, MediaLinkRole.Video, 0, 17),
            (MediaLinkContext.CareEntry, 1, MediaLinkRole.Video, 1, 16),
            (MediaLinkContext.WaterChangeSession, 1, MediaLinkRole.Evidence, 0, 19),
            (MediaLinkContext.DiscussionPost, 1, MediaLinkRole.Attachment, 0, 18),
            (MediaLinkContext.Profile, 1, MediaLinkRole.Avatar, 0, 11),
            (MediaLinkContext.Profile, 1, MediaLinkRole.Header, 0, 12),
        ];
        var actual = links.Select(l => (l.ContextType, Key: ContextKey(l), l.Role, l.Position, Item: l.MediaItemId))
            .OrderBy(x => x.ContextType).ThenBy(x => x.Role).ThenBy(x => x.Position).ToArray();
        Assert.Equal(expected.OrderBy(x => x.Context).ThenBy(x => x.Role).ThenBy(x => x.Position).ToArray(), actual);
    }

    [Fact]
    public async Task ProvenanceMovesToTheMediaItem()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync(SeedAttachments);

        await migrator.MigrateAsync();

        var items = await context.MediaItems.AsNoTracking().ToDictionaryAsync(m => m.Id);
        Assert.True(items[13].CapturedInApp);
        Assert.True(items[19].CapturedInApp);
        Assert.Equal(DateTimeOffset.Parse("2026-01-02T10:05:00Z"), items[19].ReceivedAt);
        Assert.False(items[10].CapturedInApp);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T10:00:00Z"), items[10].ReceivedAt);
    }

    [Fact]
    public async Task TheOldAttachmentTablesAndColumnsAreGone()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        var tables = await context.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name IN
                ('tank_media', 'post_media', 'care_photo', 'care_video', 'water_change_session_photo', 'discussion_post_media')
            """).ToListAsync();
        var columns = await context.Database.SqlQuery<string>($"""
            SELECT table_name || '.' || column_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name IN ('main_media_id', 'avatar_media_id', 'profile_image_media_id')
            """).ToListAsync();

        Assert.Empty(tables);
        Assert.Empty(columns);
    }

    [Fact]
    public async Task DownRestoresTheOldTablesWithTheirRows()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync(SeedAttachments);
        await migrator.MigrateAsync();

        await migrator.MigrateAsync(PreviousMigration);

        Assert.Equal(8, await CountAsync(context, "tank_media"));
        Assert.Equal(2, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.tank_media WHERE section IN ('decommission', 'recommission') AND captured_in_app").SingleAsync());
        Assert.Equal(2, await CountAsync(context, "post_media"));
        Assert.Equal(2, await CountAsync(context, "care_photo"));
        Assert.Equal(2, await CountAsync(context, "care_video"));
        Assert.Equal(1, await CountAsync(context, "water_change_session_photo"));
        Assert.Equal(1, await CountAsync(context, "discussion_post_media"));
        Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.tank WHERE main_media_id = 10").SingleAsync());
        Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.profile WHERE avatar_media_id = 11 AND profile_image_media_id = 12").SingleAsync());
        Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.tank_media WHERE media_item_id = 13 AND section = 'time_lapse' AND captured_in_app").SingleAsync());
        Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.water_change_session_photo WHERE captured_in_app AND received_at = '2026-01-02T10:05:00Z'").SingleAsync());
    }

    [Fact]
    public async Task DeletingAContextDeletesItsLinksAndKeepsTheMediaItems()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        var item = new MediaItem { OwnerProfileId = owner.Id, Kind = MediaKind.Photo, StorageKey = $"k-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        context.MediaItems.Add(item);
        await context.SaveChangesAsync();
        context.MediaLinks.Add(MediaLink.ForTank(tank.Id, item.Id, MediaLinkRole.Showcase));
        await context.SaveChangesAsync();

        await context.Tanks.Where(t => t.Id == tank.Id).ExecuteDeleteAsync();

        Assert.Empty(await context.MediaLinks.Where(l => l.MediaItemId == item.Id).ToListAsync());
        Assert.True(await context.MediaItems.AnyAsync(m => m.Id == item.Id));
    }

    [Theory]
    [InlineData("tank", "photo")]
    [InlineData("tank", "avatar")]
    [InlineData("profile", "main")]
    [InlineData("profile", "showcase")]
    public async Task TheDatabaseRefusesARoleThatDoesNotBelongToTheContext(string contextType, string role)
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        var itemId = await CreateItemAsync(context, owner.Id);
        long? tankKey = contextType == "tank" ? tank.Id : null;
        long? profileKey = contextType == "profile" ? owner.Id : null;
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.media_link (media_item_id, context_type, role, position, tank_id, profile_id)
            VALUES ({itemId}, {contextType}, {role}, 0, {tankKey}, {profileKey})
            """));
        Assert.Equal("ck_media_link_role", exception.ConstraintName);
    }

    [Fact]
    public async Task TheDatabaseRefusesALinkWithoutExactlyTheKeyOfItsContext()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        var itemId = await CreateItemAsync(context, owner.Id);

        var wrongKey = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.media_link (media_item_id, context_type, role, position, profile_id)
            VALUES ({itemId}, 'tank', 'showcase', 0, {owner.Id})
            """));
        Assert.Equal("ck_media_link_context_key", wrongKey.ConstraintName);

        var twoKeys = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.media_link (media_item_id, context_type, role, position, tank_id, profile_id)
            VALUES ({itemId}, 'tank', 'showcase', 0, {tank.Id}, {owner.Id})
            """));
        Assert.Equal("ck_media_link_context_key", twoKeys.ConstraintName);

        var noKey = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.media_link (media_item_id, context_type, role, position)
            VALUES ({itemId}, 'tank', 'showcase', 0)
            """));
        Assert.Equal("ck_media_link_context_key", noKey.ConstraintName);
    }

    [Fact]
    public async Task AProfileHasOneAvatarAndOneHeader()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var first = await CreateItemAsync(context, owner.Id);
        var second = await CreateItemAsync(context, owner.Id);
        context.MediaLinks.Add(MediaLink.ForProfile(owner.Id, first, MediaLinkRole.Avatar));
        context.MediaLinks.Add(MediaLink.ForProfile(owner.Id, first, MediaLinkRole.Header));
        await context.SaveChangesAsync();

        context.MediaLinks.Add(MediaLink.ForProfile(owner.Id, second, MediaLinkRole.Avatar));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal("ux_media_link_profile_role", Assert.IsType<PostgresException>(exception.InnerException).ConstraintName);
    }

    private static long ContextKey(MediaLink link) => link.ContextType switch
    {
        MediaLinkContext.Tank => link.TankId!.Value,
        MediaLinkContext.CareEntry => link.CareEntryId!.Value,
        MediaLinkContext.WaterChangeSession => link.WaterChangeSessionId!.Value,
        MediaLinkContext.Story => link.StoryId!.Value,
        MediaLinkContext.DiscussionPost => link.DiscussionPostId!.Value,
        MediaLinkContext.Profile => link.ProfileId!.Value,
        _ => throw new InvalidOperationException(),
    };

    private static async Task<long> CreateItemAsync(Cichlids.Infrastructure.Persistence.CichlidsDbContext context, long ownerId)
    {
        var item = new MediaItem { OwnerProfileId = ownerId, Kind = MediaKind.Photo, StorageKey = $"k-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        context.MediaItems.Add(item);
        await context.SaveChangesAsync();
        return item.Id;
    }

    // The table names are literals of this test class.
#pragma warning disable EF1002
    private static Task<int> CountAsync(Cichlids.Infrastructure.Persistence.CichlidsDbContext context, string table) =>
        context.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM public.{table}").SingleAsync();
#pragma warning restore EF1002
}
