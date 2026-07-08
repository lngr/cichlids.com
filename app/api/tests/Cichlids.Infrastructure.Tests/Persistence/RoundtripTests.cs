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
        long postId;

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
                MainMediaId = mediaItem.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow,
            };
            writeContext.Tanks.Add(tank);
            await writeContext.SaveChangesAsync();

            var post = new Post
            {
                AuthorProfileId = profile.Id,
                TankId = tank.Id,
                Kind = PostKind.Single,
                Topic = PostTopic.Tanks,
                Title = "Roundtrip Post",
                State = PostState.Published,
                CreatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow,
            };
            writeContext.Posts.Add(post);
            await writeContext.SaveChangesAsync();

            var comment = new Comment
            {
                PostId = post.Id,
                AuthorProfileId = profile.Id,
                Body = "Great tank!",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            writeContext.Comments.Add(comment);

            var rating = new Rating
            {
                PostId = post.Id,
                ProfileId = profile.Id,
                Stars = 5,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            writeContext.Ratings.Add(rating);

            await writeContext.SaveChangesAsync();

            profileId = profile.Id;
            tankId = tank.Id;
            mediaItemId = mediaItem.Id;
            postId = post.Id;
        }

        await using var readContext = _fixture.CreateContext();

        var readProfile = await readContext.Profiles.SingleAsync(x => x.Id == profileId);
        Assert.Equal(username, readProfile.Username);

        var readMediaItem = await readContext.MediaItems.SingleAsync(x => x.Id == mediaItemId);
        Assert.Equal(MediaKind.Photo, readMediaItem.Kind);

        var readTank = await readContext.Tanks.SingleAsync(x => x.Id == tankId);
        Assert.Equal(profileId, readTank.ProfileId);
        Assert.Equal(mediaItemId, readTank.MainMediaId);
        Assert.Equal(TankCategory.Tanganyika, readTank.Category);
        Assert.Equal(TankState.Published, readTank.State);

        var readPost = await readContext.Posts.SingleAsync(x => x.Id == postId);
        Assert.Equal(tankId, readPost.TankId);
        Assert.Equal(profileId, readPost.AuthorProfileId);

        var readComment = await readContext.Comments.SingleAsync(x => x.PostId == postId);
        Assert.Equal("Great tank!", readComment.Body);
        Assert.Equal(profileId, readComment.AuthorProfileId);

        var readRating = await readContext.Ratings.SingleAsync(x => x.PostId == postId);
        Assert.Equal((short)5, readRating.Stars);
        Assert.Equal(profileId, readRating.ProfileId);
    }
}
