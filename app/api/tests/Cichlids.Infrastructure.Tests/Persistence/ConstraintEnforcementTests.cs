using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
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
        var story = await TestDataBuilder.CreateStoryAsync(context, profile.Id);

        context.Comments.Add(new Comment
        {
            StoryId = story.Id,
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
        var story = await TestDataBuilder.CreateStoryAsync(context, profile.Id);

        context.Ratings.Add(new Rating
        {
            StoryId = story.Id,
            Stars = 6,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_second_rating_of_the_same_member_on_the_same_post_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        var story = await TestDataBuilder.CreateStoryAsync(context, profile.Id);
        var rater = await TestDataBuilder.CreateProfileAsync(context);

        context.Ratings.Add(new Rating { StoryId = story.Id, ProfileId = rater.Id, Stars = 3, CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();

        context.Ratings.Add(new Rating { StoryId = story.Id, ProfileId = rater.Id, Stars = 5, CreatedAt = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_second_rating_of_the_same_member_on_the_same_tank_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        var rater = await TestDataBuilder.CreateProfileAsync(context);

        context.Ratings.Add(new Rating { TankId = tank.Id, ProfileId = rater.Id, Stars = 2, CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();

        context.Ratings.Add(new Rating { TankId = tank.Id, ProfileId = rater.Id, Stars = 4, CreatedAt = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Anonymous_ratings_on_the_same_post_and_tank_are_accepted_in_any_number()
    {
        await using var context = _fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var story = await TestDataBuilder.CreateStoryAsync(context, owner.Id);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);

        context.Ratings.AddRange(
            new Rating { StoryId = story.Id, Stars = 1, CreatedAt = DateTimeOffset.UtcNow },
            new Rating { StoryId = story.Id, Stars = 2, CreatedAt = DateTimeOffset.UtcNow },
            new Rating { TankId = tank.Id, Stars = 3, CreatedAt = DateTimeOffset.UtcNow },
            new Rating { TankId = tank.Id, Stars = 4, CreatedAt = DateTimeOffset.UtcNow });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task One_member_can_rate_different_posts_and_a_post_and_a_tank()
    {
        await using var context = _fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var firstStory = await TestDataBuilder.CreateStoryAsync(context, owner.Id);
        var secondStory = await TestDataBuilder.CreateStoryAsync(context, owner.Id);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        var rater = await TestDataBuilder.CreateProfileAsync(context);

        context.Ratings.AddRange(
            new Rating { StoryId = firstStory.Id, ProfileId = rater.Id, Stars = 1, CreatedAt = DateTimeOffset.UtcNow },
            new Rating { StoryId = secondStory.Id, ProfileId = rater.Id, Stars = 2, CreatedAt = DateTimeOffset.UtcNow },
            new Rating { TankId = tank.Id, ProfileId = rater.Id, Stars = 3, CreatedAt = DateTimeOffset.UtcNow });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task A_second_canonical_slug_alias_for_the_same_post_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        var story = await TestDataBuilder.CreateStoryAsync(context, profile.Id);

        context.SlugAliases.Add(new SlugAlias
        {
            StoryId = story.Id,
            Value = $"first-{Guid.NewGuid():N}",
            IsCanonical = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        context.SlugAliases.Add(new SlugAlias
        {
            StoryId = story.Id,
            Value = $"second-{Guid.NewGuid():N}",
            IsCanonical = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_second_species_with_the_same_slug_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var slug = $"slug-{Guid.NewGuid():N}";
        context.Species.Add(NewSpecies(slug));
        await context.SaveChangesAsync();

        context.Species.Add(NewSpecies(slug));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Species_without_a_slug_are_accepted_in_any_number()
    {
        await using var context = _fixture.CreateContext();
        context.Species.AddRange(NewSpecies(null), NewSpecies(null));

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task A_slug_alias_value_already_used_by_another_post_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        var firstStory = await TestDataBuilder.CreateStoryAsync(context, profile.Id);
        var secondStory = await TestDataBuilder.CreateStoryAsync(context, profile.Id);
        var value = $"alias-{Guid.NewGuid():N}";

        context.SlugAliases.Add(new SlugAlias
        {
            StoryId = firstStory.Id,
            Value = value,
            IsCanonical = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        context.SlugAliases.Add(new SlugAlias
        {
            StoryId = secondStory.Id,
            Value = value,
            IsCanonical = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static Species NewSpecies(string? slug) => new()
    {
        Genus = $"Genus{Guid.NewGuid():N}",
        Name = $"name{Guid.NewGuid():N}",
        DisplayName = "Test species",
        Slug = slug,
        CreatedAt = DateTimeOffset.UtcNow,
    };

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

    [Fact]
    public async Task Discussion_post_with_an_author_profile_and_a_poster_name_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        var thread = await CreateThreadAsync(context);

        context.DiscussionPosts.Add(new DiscussionPost
        {
            ThreadId = thread.Id,
            AuthorProfileId = profile.Id,
            PosterName = "Guest Name",
            Body = "author and poster name",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Discussion_post_by_a_guest_without_an_author_profile_is_accepted()
    {
        await using var context = _fixture.CreateContext();
        var thread = await CreateThreadAsync(context);

        context.DiscussionPosts.Add(new DiscussionPost
        {
            ThreadId = thread.Id,
            PosterName = "Guest Name",
            Body = "guest post",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Discussion_post_by_a_member_without_a_poster_name_is_accepted()
    {
        await using var context = _fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        var thread = await CreateThreadAsync(context);

        context.DiscussionPosts.Add(new DiscussionPost
        {
            ThreadId = thread.Id,
            AuthorProfileId = profile.Id,
            Body = "member post",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync();
    }

    private static async Task<DiscussionThread> CreateThreadAsync(CichlidsDbContext context)
    {
        var thread = new DiscussionThread
        {
            Category = DiscussionCategory.Cichlids,
            Title = $"Thread {Guid.NewGuid():N}",
            State = DiscussionThreadState.Open,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        context.DiscussionThreads.Add(thread);
        await context.SaveChangesAsync();
        return thread;
    }
}
