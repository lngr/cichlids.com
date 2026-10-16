using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class StorySubjectTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AStoryHasSubjectsOfEveryTypeWithOrWithoutAPosition()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        var inhabitant = new Inhabitant { TankId = tank.Id, Count = 3 };
        var species = new Species { Genus = $"G{Guid.NewGuid():N}", Name = "test" };
        context.AddRange(inhabitant, species);
        await context.SaveChangesAsync();
        var story = await TestDataBuilder.CreateStoryAsync(context, owner.Id);

        story.Subjects.AddRange([
            StorySubject.ForTank(tank.Id, position: 1),
            StorySubject.ForInhabitant(inhabitant.Id),
            StorySubject.ForSpecies(species.Id, position: 3)]);
        await context.SaveChangesAsync();

        await using var read = fixture.CreateContext();
        var subjects = await read.StorySubjects.Where(s => s.StoryId == story.Id).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal([StorySubjectType.Tank, StorySubjectType.Inhabitant, StorySubjectType.Species], subjects.Select(s => s.SubjectType));
        Assert.Equal([1, null, 3], subjects.Select(s => s.Position));
    }

    [Fact]
    public async Task TheDatabaseRefusesASubjectWithoutExactlyTheKeyOfItsType()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        var story = await TestDataBuilder.CreateStoryAsync(context, owner.Id);

        var noKey = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.story_subject (story_id, subject_type) VALUES ({story.Id}, 'tank')
            """));
        Assert.Equal("ck_story_subject_key", noKey.ConstraintName);

        var wrongKey = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.story_subject (story_id, subject_type, tank_id) VALUES ({story.Id}, 'species', {tank.Id})
            """));
        Assert.Equal("ck_story_subject_key", wrongKey.ConstraintName);

        var unknownType = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.story_subject (story_id, subject_type, tank_id) VALUES ({story.Id}, 'profile', {tank.Id})
            """));
        Assert.Contains(unknownType.ConstraintName, new[] { "ck_story_subject_type", "ck_story_subject_key" });
    }

    [Fact]
    public async Task AStoryPointsToATargetOnceAndUsesAPositionOnce()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        var other = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        var story = await TestDataBuilder.CreateStoryAsync(context, owner.Id);
        story.Subjects.Add(StorySubject.ForTank(tank.Id, position: 2));
        await context.SaveChangesAsync();

        await using var duplicateTarget = fixture.CreateContext();
        duplicateTarget.StorySubjects.Add(new StorySubject { StoryId = story.Id, SubjectType = StorySubjectType.Tank, TankId = tank.Id });
        var target = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateTarget.SaveChangesAsync());
        Assert.Equal("ux_story_subject_story_tank", Assert.IsType<PostgresException>(target.InnerException).ConstraintName);

        await using var duplicatePosition = fixture.CreateContext();
        duplicatePosition.StorySubjects.Add(new StorySubject { StoryId = story.Id, SubjectType = StorySubjectType.Tank, TankId = other.Id, Position = 2 });
        var position = await Assert.ThrowsAsync<DbUpdateException>(() => duplicatePosition.SaveChangesAsync());
        Assert.Equal("ux_story_subject_story_position", Assert.IsType<PostgresException>(position.InnerException).ConstraintName);
    }

    [Fact]
    public async Task DeletingTheTankOrTheStoryRemovesTheSubject()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        var story = await TestDataBuilder.CreateStoryAsync(context, owner.Id);
        var other = await TestDataBuilder.CreateStoryAsync(context, owner.Id);
        context.StorySubjects.AddRange(
            new StorySubject { StoryId = story.Id, SubjectType = StorySubjectType.Tank, TankId = tank.Id },
            new StorySubject { StoryId = other.Id, SubjectType = StorySubjectType.Tank, TankId = tank.Id });
        await context.SaveChangesAsync();

        await context.Stories.Where(s => s.Id == other.Id).ExecuteDeleteAsync();
        Assert.Equal(1, await context.StorySubjects.CountAsync(s => s.TankId == tank.Id));

        await context.Tanks.Where(t => t.Id == tank.Id).ExecuteDeleteAsync();
        Assert.False(await context.StorySubjects.AnyAsync(s => s.StoryId == story.Id));
    }

    [Fact]
    public async Task AStoryWithoutCoverShowsItsFirstItemAndRemovingTheCoverLinkClearsTheChoice()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var story = await TestDataBuilder.CreateStoryAsync(context, owner.Id);
        Assert.Null(story.CoverMediaLinkId);

        var first = await CreateItemAsync(context, owner.Id);
        var second = await CreateItemAsync(context, owner.Id);
        var firstLink = MediaLink.ForStory(story.Id, first, 0);
        var secondLink = MediaLink.ForStory(story.Id, second, 1);
        context.MediaLinks.AddRange(firstLink, secondLink);
        await context.SaveChangesAsync();

        story.CoverMediaLinkId = secondLink.Id;
        await context.SaveChangesAsync();
        Assert.Equal(secondLink.Id, await context.Stories.Where(s => s.Id == story.Id).Select(s => s.CoverMediaLinkId).SingleAsync());

        await context.MediaLinks.Where(l => l.Id == secondLink.Id).ExecuteDeleteAsync();
        Assert.Null(await context.Stories.Where(s => s.Id == story.Id).Select(s => s.CoverMediaLinkId).SingleAsync());
        Assert.True(await context.MediaLinks.AnyAsync(l => l.Id == firstLink.Id));
    }

    [Fact]
    public async Task TheItemsOfAStoryUseEachPositionOnce()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var story = await TestDataBuilder.CreateStoryAsync(context, owner.Id);
        context.MediaLinks.Add(MediaLink.ForStory(story.Id, await CreateItemAsync(context, owner.Id), 0));
        await context.SaveChangesAsync();

        await using var other = fixture.CreateContext();
        other.MediaLinks.Add(MediaLink.ForStory(story.Id, await CreateItemAsync(other, owner.Id), 0));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        Assert.Equal("ux_media_link_story_position", Assert.IsType<PostgresException>(exception.InnerException).ConstraintName);
    }

    private static async Task<long> CreateItemAsync(CichlidsDbContext context, long ownerId)
    {
        var item = new MediaItem { OwnerProfileId = ownerId, Kind = MediaKind.Photo, StorageKey = $"k-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        context.MediaItems.Add(item);
        await context.SaveChangesAsync();
        return item.Id;
    }
}
