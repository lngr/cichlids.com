using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;

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
}
