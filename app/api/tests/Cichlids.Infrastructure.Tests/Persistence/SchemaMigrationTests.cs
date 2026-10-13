using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Trait("Category", "Docker")]
[Collection(PostgresCollection.Name)]
public sealed class SchemaMigrationTests
{
    private static readonly string[] ExpectedPublicTables =
    [
        "profile", "profile_identity",
        "species", "species_common_name", "species_link",
        "tank", "inhabitant", "tank_media",
        "media_item", "media_variant",
        "post", "post_media", "post_species",
        "comment", "rating", "comment_vote",
        "collection", "collection_entry",
        "follow", "slug_alias",
        "outbox_event", "webhook_subscription",
        "discussion_thread", "discussion_post", "discussion_post_media",
    ];

    // legacy_id carries a unique index on every migrated aggregate; this checks a sample that
    // spans every area of the schema rather than every single table.
    private static readonly string[] TablesWithUniqueLegacyIdIndex =
    [
        "profile", "post", "comment", "media_item", "tank", "species", "collection",
        "discussion_thread", "discussion_post",
    ];

    private readonly PostgresFixture _fixture;

    public SchemaMigrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migrated_database_has_every_expected_public_table()
    {
        var tables = await _fixture.QueryStringsAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'");

        foreach (var expectedTable in ExpectedPublicTables)
        {
            Assert.Contains(expectedTable, tables);
        }
    }

    [Fact]
    public async Task Migrated_database_has_the_archive_legacy_comment_table()
    {
        var tables = await _fixture.QueryStringsAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'archive'");

        Assert.Contains("legacy_comment", tables);
    }

    [Theory]
    [MemberData(nameof(LegacyIdIndexCases))]
    public async Task Table_has_a_unique_index_on_legacy_id(string table)
    {
        var indexDefinitions = await _fixture.QueryStringsAsync(
            $"SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = '{table}'");

        Assert.Contains(indexDefinitions, def =>
            def.Contains("UNIQUE", StringComparison.Ordinal) &&
            def.Contains("(legacy_id)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Outbox_event_has_a_partial_index_on_the_next_attempt_of_open_events()
    {
        var indexDefinitions = await _fixture.QueryStringsAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'outbox_event'");

        Assert.Contains(indexDefinitions, def =>
            def.Contains("ix_outbox_event_due", StringComparison.Ordinal) &&
            def.Contains("(next_attempt_at)", StringComparison.Ordinal) &&
            def.Contains("WHERE ((dispatched_at IS NULL) AND (given_up_at IS NULL))", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Outbox_event_stores_acknowledged_subscriptions_as_a_non_null_bigint_array()
    {
        var columns = await _fixture.QueryStringsAsync(
            "SELECT udt_name || ':' || is_nullable FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'outbox_event' AND column_name = 'acknowledged_subscription_ids'");

        Assert.Equal(["_int8:NO"], columns);
    }

    [Fact]
    public async Task Model_has_no_changes_missing_from_the_migrations()
    {
        await using var context = _fixture.CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Outbox_redelivery_migration_marks_events_that_gave_up_as_given_up()
    {
        await using var database = await _fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrations = context.Database.GetMigrations().ToList();
        var target = migrations.FindIndex(m => m.EndsWith("_AddOutboxRedeliverySchedule", StringComparison.Ordinal));
        Assert.True(target > 0);

        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[target - 1]);

        var givenUpId = Guid.CreateVersion7();
        var pendingId = Guid.CreateVersion7();
        var occurredAt = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        await context.Database.ExecuteSqlAsync(
            $$"""
            INSERT INTO outbox_event (id, occurred_at, event_type, aggregate_type, aggregate_id, payload, dispatched_at, attempt_count, last_error)
            VALUES
                ({{givenUpId}}, {{occurredAt}}, 'comment.created', 'post', '1', '{}'::jsonb, {{occurredAt.AddHours(2)}}, 10, 'gave up'),
                ({{pendingId}}, {{occurredAt}}, 'comment.created', 'post', '2', '{}'::jsonb, NULL, 1, 'subscription 1 responded 500')
            """);

        await migrator.MigrateAsync();

        var givenUp = await context.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == givenUpId);
        Assert.Null(givenUp.DispatchedAt);
        Assert.Equal(occurredAt.AddHours(2), givenUp.GivenUpAt);

        var pending = await context.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == pendingId);
        Assert.Null(pending.GivenUpAt);
        Assert.Equal(occurredAt, pending.NextAttemptAt);
        Assert.Empty(pending.AcknowledgedSubscriptionIds);
    }

    [Fact]
    public async Task Temporary_database_is_dropped_when_disposed()
    {
        string name;
        await using (var database = await _fixture.CreateEmptyDatabaseAsync())
        {
            await using var context = database.CreateContext();
            name = context.Database.GetDbConnection().Database;
            Assert.Contains(name, await _fixture.QueryStringsAsync("SELECT datname FROM pg_database"));
        }

        Assert.DoesNotContain(name, await _fixture.QueryStringsAsync("SELECT datname FROM pg_database"));
    }

    public static IEnumerable<object[]> LegacyIdIndexCases()
    {
        return TablesWithUniqueLegacyIdIndex.Select(table => new object[] { table });
    }
}
