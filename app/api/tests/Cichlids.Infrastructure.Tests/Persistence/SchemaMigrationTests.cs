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
        "post", "post_media",
        "comment", "rating", "comment_vote",
        "collection", "collection_entry",
        "follow", "slug_alias",
        "outbox_event", "webhook_subscription",
    ];

    // legacy_id carries a unique index on every migrated aggregate; this checks a sample that
    // spans every area of the schema rather than every single table.
    private static readonly string[] TablesWithUniqueLegacyIdIndex =
    [
        "profile", "post", "comment", "media_item", "tank", "species", "collection",
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

    public static IEnumerable<object[]> LegacyIdIndexCases()
    {
        return TablesWithUniqueLegacyIdIndex.Select(table => new object[] { table });
    }
}
