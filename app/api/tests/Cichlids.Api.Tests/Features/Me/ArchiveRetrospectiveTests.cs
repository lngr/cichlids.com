// Story: TASK-8.2
using System.Net;
using System.Text.Json;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Me;

/// <summary>
/// The archive retrospective of a member: which cards the archive data of the member yields, which
/// data stays out, who receives one, and how the member marks it as seen.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-8.2")]
public sealed class ArchiveRetrospectiveTests(ApiFixture fixture)
{
    private const string Route = "/api/me/retrospectives/archive";

    private static int NewLegacyId() => Random.Shared.Next(1_500_000_000, int.MaxValue);

    private async Task SetCreatedAtAsync(long profileId, DateTimeOffset createdAt)
    {
        await using var db = fixture.CreateDbContext();
        await db.Profiles.Where(p => p.Id == profileId).ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, createdAt));
    }

    private async Task<long> AddMigratedPhotoAsync(long ownerId, MediaKind kind = MediaKind.Photo, bool withProfileLink = false)
    {
        await using var db = fixture.CreateDbContext();
        var item = new MediaItem
        {
            LegacyId = NewLegacyId(), OwnerProfileId = ownerId, Kind = kind, StorageKey = $"private/test/{Guid.NewGuid():N}.jpg",
            CreatedAt = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow,
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        if (withProfileLink)
        {
            db.MediaLinks.Add(MediaLink.ForProfile(ownerId, item.Id, MediaLinkRole.Avatar));
            await db.SaveChangesAsync();
        }

        return item.Id;
    }

    private async Task<long> AddStoryAsync(long authorId, long views, bool deleted = false, bool withCover = true)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var story = new Story
        {
            LegacyId = NewLegacyId(), AuthorProfileId = authorId, Topic = StoryTopic.Cichlids, Title = $"Story {Guid.NewGuid():N}",
            State = StoryState.Published, ViewCount = views, CreatedAt = now, UpdatedAt = now, PublishedAt = now, DeletedAt = deleted ? now : null,
        };
        db.Stories.Add(story);
        await db.SaveChangesAsync();
        db.SlugAliases.Add(new SlugAlias { StoryId = story.Id, Value = $"retro-{story.Id}", IsCanonical = true, CreatedAt = now });
        if (withCover)
        {
            var itemId = await AddMigratedPhotoAsync(authorId);
            db.MediaLinks.Add(MediaLink.ForStory(story.Id, itemId));
            db.MediaVariants.Add(new MediaVariant { MediaItemId = itemId, Label = "medium", StorageKey = $"variants/medium/test-{itemId}.jpg" });
        }

        await db.SaveChangesAsync();
        return story.Id;
    }

    private async Task<Tank> AddArchiveTankAsync(long ownerId, bool adopted = false, bool deleted = false, bool withImage = false)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var tank = new Tank
        {
            LegacyId = NewLegacyId(), ProfileId = ownerId, Title = $"Archive tank {Guid.NewGuid():N}", State = TankState.Published,
            CreatedAt = now, PublishedAt = now, AdoptedAt = adopted ? now : null, DeletedAt = deleted ? now : null,
        };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        if (withImage)
        {
            var itemId = await AddMigratedPhotoAsync(ownerId);
            db.MediaLinks.Add(MediaLink.ForTank(tank.Id, itemId, MediaLinkRole.Main));
            db.MediaVariants.Add(new MediaVariant { MediaItemId = itemId, Label = "medium", StorageKey = $"variants/medium/test-{itemId}.jpg" });
            await db.SaveChangesAsync();
        }

        return tank;
    }

    private async Task AddCommentAsync(long? storyId, long? tankId, long? authorId, bool deleted = false, bool migrated = true)
    {
        await using var db = fixture.CreateDbContext();
        db.Comments.Add(new Comment
        {
            LegacyId = migrated ? NewLegacyId() : null,
            StoryId = storyId, TankId = tankId, AuthorProfileId = authorId, PosterName = authorId is null ? "Guest" : null,
            Body = "Nice", CreatedAt = DateTimeOffset.UtcNow, DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
        });
        await db.SaveChangesAsync();
    }

    private async Task AddRatingAsync(long? storyId, long? tankId, long? raterId, short stars, bool migrated = true)
    {
        await using var db = fixture.CreateDbContext();
        db.Ratings.Add(new Rating
        {
            LegacyCommentId = migrated ? NewLegacyId() : null,
            StoryId = storyId, TankId = tankId, ProfileId = raterId, Stars = stars, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<JsonElement?> GetAsync(Member member)
    {
        var response = await SendAsync(fixture, HttpMethod.Get, Route, member.Token);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task<JsonElement> MeAsync(Member member) =>
        (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, "/api/me", member.Token))).GetProperty("archiveRetrospective");

    [Fact]
    public async Task GivenAMemberWithoutArchiveData_WhenReadingTheRetrospective_ThenThereIsNone()
    {
        var member = await NewMemberAsync(fixture);
        await AddMigratedPhotoAsync(member.ProfileId, withProfileLink: true);

        Assert.Null(await GetAsync(member));
        var status = await MeAsync(member);
        Assert.False(status.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task GivenAMemberWithPhotosCommentsAndRatings_WhenReadingTheRetrospective_ThenTheCardsCarryTheCounts()
    {
        var member = await NewMemberAsync(fixture);
        var other = await NewMemberAsync(fixture);
        await SetCreatedAtAsync(member.ProfileId, new DateTimeOffset(2011, 4, 20, 10, 0, 0, TimeSpan.Zero));
        var story = await AddStoryAsync(member.ProfileId, views: 40);
        await AddMigratedPhotoAsync(member.ProfileId);
        await AddMigratedPhotoAsync(member.ProfileId, MediaKind.Video);
        var tank = await AddArchiveTankAsync(member.ProfileId);
        await AddCommentAsync(story, null, other.ProfileId);
        await AddCommentAsync(null, tank.Id, null);
        await AddRatingAsync(story, null, other.ProfileId, 5);
        await AddRatingAsync(null, tank.Id, other.ProfileId, 3);

        var body = (await GetAsync(member))!.Value;

        Assert.Equal(2011, body.GetProperty("memberSince").GetProperty("year").GetInt32());
        Assert.Equal(4, body.GetProperty("memberSince").GetProperty("month").GetInt32());
        var stats = body.GetProperty("stats");
        Assert.Equal(2, stats.GetProperty("photos").GetInt32());
        Assert.Equal(2, stats.GetProperty("comments").GetInt32());
        Assert.Equal(8, stats.GetProperty("stars").GetInt32());
        Assert.Equal(1, body.GetProperty("tanks").GetArrayLength());
        var status = await MeAsync(member);
        Assert.True(status.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("seenAt").ValueKind);
    }

    [Fact]
    public async Task GivenTheUnknownCreationMarker_WhenReadingTheRetrospective_ThenThereIsNoMemberSinceCard()
    {
        var member = await NewMemberAsync(fixture);
        await SetCreatedAtAsync(member.ProfileId, LegacyProfiles.UnknownCreatedAt);
        await AddMigratedPhotoAsync(member.ProfileId);

        var body = (await GetAsync(member))!.Value;

        Assert.Equal(JsonValueKind.Null, body.GetProperty("memberSince").ValueKind);
        Assert.Equal(1, body.GetProperty("stats").GetProperty("photos").GetInt32());
    }

    [Fact]
    public async Task GivenStoriesWithViews_WhenReadingTheRetrospective_ThenTheMostViewedStoryHasItsCoverAndCount()
    {
        var member = await NewMemberAsync(fixture);
        await AddStoryAsync(member.ProfileId, views: 10);
        var top = await AddStoryAsync(member.ProfileId, views: 900);
        await AddStoryAsync(member.ProfileId, views: 5000, deleted: true);

        var most = (await GetAsync(member))!.Value.GetProperty("mostViewed");

        Assert.Equal(top, most.GetProperty("storyId").GetInt64());
        Assert.Equal(900, most.GetProperty("viewCount").GetInt64());
        Assert.Equal($"retro-{top}", most.GetProperty("slug").GetString());
        Assert.False(string.IsNullOrEmpty(most.GetProperty("image").GetProperty("medium").GetString()));
    }

    [Fact]
    public async Task GivenStoriesWithoutViews_WhenReadingTheRetrospective_ThenThereIsNoMostViewedCard()
    {
        var member = await NewMemberAsync(fixture);
        await AddStoryAsync(member.ProfileId, views: 0);

        var body = (await GetAsync(member))!.Value;

        Assert.Equal(JsonValueKind.Null, body.GetProperty("mostViewed").ValueKind);
        Assert.Equal(1, body.GetProperty("stats").GetProperty("photos").GetInt32());
    }

    [Fact]
    public async Task GivenAdoptedAndDeletedArchiveTanks_WhenReadingTheRetrospective_ThenOnlyTheOpenOnesAreOffered()
    {
        var member = await NewMemberAsync(fixture);
        var open = await AddArchiveTankAsync(member.ProfileId, withImage: true);
        await AddArchiveTankAsync(member.ProfileId, adopted: true);
        await AddArchiveTankAsync(member.ProfileId, deleted: true);
        await AddStoryAsync(member.ProfileId, views: 1);

        var tanks = (await GetAsync(member))!.Value.GetProperty("tanks");

        Assert.Equal(1, tanks.GetArrayLength());
        Assert.Equal(open.Id, tanks[0].GetProperty("id").GetInt64());
        Assert.Equal(open.Title, tanks[0].GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(tanks[0].GetProperty("image").GetProperty("medium").GetString()));
    }

    [Fact]
    public async Task GivenOnlyAdoptedArchiveTanks_WhenReadingTheRetrospective_ThenThereIsNoTankCardAndNoRetrospective()
    {
        var member = await NewMemberAsync(fixture);
        await AddArchiveTankAsync(member.ProfileId, adopted: true);

        Assert.Null(await GetAsync(member));
    }

    [Fact]
    public async Task GivenDeletedAndOwnComments_WhenReadingTheRetrospective_ThenOnlyCommentsOfOthersCount()
    {
        var member = await NewMemberAsync(fixture);
        var other = await NewMemberAsync(fixture);
        var story = await AddStoryAsync(member.ProfileId, views: 1);
        await AddCommentAsync(story, null, other.ProfileId);
        await AddCommentAsync(story, null, other.ProfileId, deleted: true);
        await AddCommentAsync(story, null, member.ProfileId);
        var foreignStory = await AddStoryAsync(other.ProfileId, views: 1);
        await AddCommentAsync(foreignStory, null, other.ProfileId);

        var stats = (await GetAsync(member))!.Value.GetProperty("stats");

        Assert.Equal(1, stats.GetProperty("comments").GetInt32());
        Assert.Equal(0, stats.GetProperty("stars").GetInt32());
    }

    [Fact]
    public async Task GivenReactionsWrittenInTheApp_WhenReadingTheRetrospective_ThenOnlyTheMigratedOnesCount()
    {
        var member = await NewTrackedMemberAsync();
        var other = await NewTrackedMemberAsync();
        var story = await AddStoryAsync(member.ProfileId, views: 1);
        await AddCommentAsync(story, null, other.ProfileId);
        await AddCommentAsync(story, null, other.ProfileId, migrated: false);
        await AddRatingAsync(story, null, other.ProfileId, 4);
        await AddRatingAsync(story, null, (await NewTrackedMemberAsync()).ProfileId, 5, migrated: false);

        var stats = (await GetAsync(member))!.Value.GetProperty("stats");

        Assert.Equal(1, stats.GetProperty("comments").GetInt32());
        Assert.Equal(4, stats.GetProperty("stars").GetInt32());
    }

    [Fact]
    public async Task GivenOnlyReactionsOnNewAppContent_WhenReadingTheRetrospective_ThenThereIsNone()
    {
        var member = await NewTrackedMemberAsync();
        var other = await NewTrackedMemberAsync();
        long storyId;
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var story = new Story { AuthorProfileId = member.ProfileId, Topic = StoryTopic.Cichlids, State = StoryState.Published, CreatedAt = now, UpdatedAt = now, PublishedAt = now };
            db.Stories.Add(story);
            await db.SaveChangesAsync();
            storyId = story.Id;
        }
        await AddCommentAsync(storyId, null, other.ProfileId, migrated: false);
        await AddRatingAsync(storyId, null, other.ProfileId, 5, migrated: false);

        Assert.Null(await GetAsync(member));
        Assert.False((await MeAsync(member)).GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task GivenOnlyNewAppContent_WhenReadingTheRetrospective_ThenThereIsNone()
    {
        var member = await NewMemberAsync(fixture);
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.Tanks.Add(new Tank { ProfileId = member.ProfileId, Title = "New tank", State = TankState.Published, CreatedAt = now, PublishedAt = now });
            db.Stories.Add(new Story { AuthorProfileId = member.ProfileId, Topic = StoryTopic.Cichlids, State = StoryState.Published, ViewCount = 5, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        Assert.Null(await GetAsync(member));
    }

    [Fact]
    public async Task GivenAnUnseenRetrospective_WhenMarkingItSeen_ThenTheTimeIsStoredOnceAndMeReportsIt()
    {
        var member = await NewMemberAsync(fixture);
        await AddMigratedPhotoAsync(member.ProfileId);

        var first = await SendAsync(fixture, HttpMethod.Post, Route + "/seen", member.Token);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        await using var db = fixture.CreateDbContext();
        var seenAt = (await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == member.ProfileId)).ArchiveRetrospectiveSeenAt;
        Assert.NotNull(seenAt);

        var second = await SendAsync(fixture, HttpMethod.Post, Route + "/seen", member.Token);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(seenAt, (await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == member.ProfileId)).ArchiveRetrospectiveSeenAt);

        var status = await MeAsync(member);
        Assert.True(status.GetProperty("available").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, status.GetProperty("seenAt").ValueKind);
        Assert.NotNull(await GetAsync(member));
    }
}
