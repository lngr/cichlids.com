using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Trait("Category", "Docker")]
[Collection(PostgresCollection.Name)]
public sealed class ConstraintEnforcementTests
{
    private readonly PostgresFixture _fixture;

    public ConstraintEnforcementTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Comment_with_both_a_post_and_a_tank_target_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, profile.Id);
        var post = await TestDataBuilder.CreatePostAsync(context, profile.Id);

        context.Comments.Add(new Comment
        {
            PostId = post.Id,
            TankId = tank.Id,
            Body = "both targets set",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Comment_with_neither_a_post_nor_a_tank_target_is_rejected()
    {
        await using var context = _fixture.CreateContext();

        context.Comments.Add(new Comment
        {
            Body = "no target set",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Rating_with_six_stars_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        var post = await TestDataBuilder.CreatePostAsync(context, profile.Id);

        context.Ratings.Add(new Rating
        {
            PostId = post.Id,
            Stars = 6,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_second_canonical_slug_alias_for_the_same_post_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        var post = await TestDataBuilder.CreatePostAsync(context, profile.Id);

        context.SlugAliases.Add(new SlugAlias
        {
            PostId = post.Id,
            Value = $"first-{Guid.NewGuid():N}",
            IsCanonical = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        context.SlugAliases.Add(new SlugAlias
        {
            PostId = post.Id,
            Value = $"second-{Guid.NewGuid():N}",
            IsCanonical = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    // The enum converter never produces a value outside the check constraint's allowed set
    // through normal EF Core usage, so this goes around it with a raw insert to prove the
    // database itself, not just the application, rejects an invalid discussion category.
    [Fact]
    public async Task Discussion_thread_with_an_invalid_category_is_rejected_by_the_database()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO discussion_thread (category, title, state, created_at, post_count)
            VALUES ('not_a_category', 'Invalid category thread', 'archived', now(), 0)
            """,
            connection);

        await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }
}
