using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Trait("Category", "Docker")]
[Collection(PostgresCollection.Name)]
public sealed class RoundtripTests
{
    private readonly PostgresFixture _fixture;

    public RoundtripTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Profile_tank_media_post_comment_and_rating_roundtrip_through_the_db_context()
    {
        var username = $"roundtrip-{Guid.NewGuid():N}";
        long profileId;
        long tankId;
        long mediaItemId;
        long storyId;

        await using (var writeContext = _fixture.CreateContext())
        {
            var profile = new Profile
            {
                Username = username,
                DisplayName = "Roundtrip Tester",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            writeContext.Profiles.Add(profile);
            await writeContext.SaveChangesAsync();

            var mediaItem = new MediaItem
            {
                OwnerProfileId = profile.Id,
                Kind = MediaKind.Photo,
                StorageKey = $"photos/{Guid.NewGuid():N}.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            writeContext.MediaItems.Add(mediaItem);
            await writeContext.SaveChangesAsync();

            var tank = new Tank
            {
                ProfileId = profile.Id,
                Title = "Roundtrip Tank",
                Category = TankCategory.Tanganyika,
                State = TankState.Published,
                CreatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow,
            };
            writeContext.Tanks.Add(tank);
            await writeContext.SaveChangesAsync();
            writeContext.MediaLinks.Add(MediaLink.ForTank(tank.Id, mediaItem.Id, MediaLinkRole.Main));
            await writeContext.SaveChangesAsync();

            var story = new Story
            {
                AuthorProfileId = profile.Id,
                Subjects = [StorySubject.ForTank(tank.Id)],
                Topic = StoryTopic.Tanks,
                Title = "Roundtrip Story",
                State = StoryState.Published,
                CreatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow,
            };
            writeContext.Stories.Add(story);
            await writeContext.SaveChangesAsync();

            var comment = new Comment
            {
                StoryId = story.Id,
                AuthorProfileId = profile.Id,
                Body = "Great tank!",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            writeContext.Comments.Add(comment);

            var rating = new Rating
            {
                StoryId = story.Id,
                ProfileId = profile.Id,
                Stars = 5,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            writeContext.Ratings.Add(rating);

            await writeContext.SaveChangesAsync();

            profileId = profile.Id;
            tankId = tank.Id;
            mediaItemId = mediaItem.Id;
            storyId = story.Id;
        }

        await using var readContext = _fixture.CreateContext();

        var readProfile = await readContext.Profiles.SingleAsync(x => x.Id == profileId);
        Assert.Equal(username, readProfile.Username);

        var readMediaItem = await readContext.MediaItems.SingleAsync(x => x.Id == mediaItemId);
        Assert.Equal(MediaKind.Photo, readMediaItem.Kind);

        var readTank = await readContext.Tanks.SingleAsync(x => x.Id == tankId);
        Assert.Equal(profileId, readTank.ProfileId);
        Assert.Equal(mediaItemId, (await readContext.MediaLinks.SingleAsync(x => x.TankId == tankId && x.Role == MediaLinkRole.Main)).MediaItemId);
        Assert.Equal(TankCategory.Tanganyika, readTank.Category);
        Assert.Equal(TankState.Published, readTank.State);

        var readStory = await readContext.Stories.SingleAsync(x => x.Id == storyId);
        Assert.Equal(tankId, (await readContext.StorySubjects.SingleAsync(x => x.StoryId == storyId)).TankId);
        Assert.Equal(profileId, readStory.AuthorProfileId);

        var readComment = await readContext.Comments.SingleAsync(x => x.StoryId == storyId);
        Assert.Equal("Great tank!", readComment.Body);
        Assert.Equal(profileId, readComment.AuthorProfileId);

        var readRating = await readContext.Ratings.SingleAsync(x => x.StoryId == storyId);
        Assert.Equal((short)5, readRating.Stars);
        Assert.Equal(profileId, readRating.ProfileId);
    }

    [Fact]
    public async Task Discussion_thread_post_and_post_media_roundtrip_through_the_db_context()
    {
        var username = $"roundtrip-forum-{Guid.NewGuid():N}";
        long threadId;
        long storyId;
        long mediaItemId;

        await using (var writeContext = _fixture.CreateContext())
        {
            var profile = new Profile
            {
                Username = username,
                DisplayName = "Roundtrip Forum Tester",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            writeContext.Profiles.Add(profile);
            await writeContext.SaveChangesAsync();

            var mediaItem = new MediaItem
            {
                OwnerProfileId = profile.Id,
                Kind = MediaKind.Photo,
                StorageKey = $"forum_attachments/{Guid.NewGuid():N}.jpg",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            writeContext.MediaItems.Add(mediaItem);
            await writeContext.SaveChangesAsync();

            var thread = new DiscussionThread
            {
                Category = DiscussionCategory.African,
                Title = "Roundtrip Thread",
                State = DiscussionThreadState.Archived,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            writeContext.DiscussionThreads.Add(thread);
            await writeContext.SaveChangesAsync();

            var story = new DiscussionPost
            {
                ThreadId = thread.Id,
                AuthorProfileId = profile.Id,
                Body = "Roundtrip story body",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            writeContext.DiscussionPosts.Add(story);
            await writeContext.SaveChangesAsync();

            writeContext.MediaLinks.Add(MediaLink.ForDiscussionPost(story.Id, mediaItem.Id));
            await writeContext.SaveChangesAsync();

            threadId = thread.Id;
            storyId = story.Id;
            mediaItemId = mediaItem.Id;
        }

        await using var readContext = _fixture.CreateContext();

        var readThread = await readContext.DiscussionThreads.SingleAsync(x => x.Id == threadId);
        Assert.Equal(DiscussionCategory.African, readThread.Category);
        Assert.Equal(DiscussionThreadState.Archived, readThread.State);

        var readStory = await readContext.DiscussionPosts.SingleAsync(x => x.Id == storyId);
        Assert.Equal(threadId, readStory.ThreadId);
        Assert.Equal("Roundtrip story body", readStory.Body);

        var readMedia = await readContext.MediaLinks.SingleAsync(x => x.DiscussionPostId == storyId);
        Assert.Equal(mediaItemId, readMedia.MediaItemId);
    }
}
