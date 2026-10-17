using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Stories;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Comments;

[Collection(ApiCollection.Name)]
public class CommentWriteEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task PostStoryComment_AnonymousReturnsUnauthorized()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            $"/api/stories/{fixture.Seed.PublishedPictureACanonicalSlug}/comments",
            new CreateCommentRequest("hi", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostStoryComment_BodyAndStarsSplitIntoCommentAndRatingWithAggregatesAndOutbox()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"writer-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest("What a fish!", 4));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = JsonSerializer.Deserialize<CommentCreatedDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.NotNull(created.Comment);
        Assert.Equal("What a fish!", created.Comment!.Body);
        Assert.NotNull(created.Comment.Author);
        Assert.Null(created.Comment.PosterName);
        Assert.NotNull(created.Rating);
        Assert.Equal(4, created.Rating!.Stars);

        await using (var db = fixture.CreateDbContext())
        {
            var comment = await db.Comments.SingleAsync(c => c.Id == created.Comment.Id);
            Assert.Equal(storyId, comment.StoryId);
            Assert.Null(comment.TankId);
            Assert.Equal(created.Comment.Author!.Id, comment.AuthorProfileId);

            var rating = await db.Ratings.SingleAsync(r => r.Id == created.Rating.Id);
            Assert.Equal(storyId, rating.StoryId);
            Assert.Equal((short)4, rating.Stars);
            Assert.Equal(comment.AuthorProfileId, rating.ProfileId);
            Assert.Equal(comment.Id, rating.CommentId);

            var commentEvent = await db.OutboxEvents.SingleAsync(
                e => e.EventType == "comment.created" && e.AggregateType == "story" && e.AggregateId == storyId.ToString());
            using (var commentPayload = JsonDocument.Parse(commentEvent.Payload))
            {
                Assert.Equal(created.Comment.Id, commentPayload.RootElement.GetProperty("commentId").GetInt64());
                Assert.Equal(storyId, commentPayload.RootElement.GetProperty("storyId").GetInt64());
            }

            Assert.Null(commentEvent.DispatchedAt);
            Assert.Equal(commentEvent.OccurredAt, commentEvent.NextAttemptAt);

            var ratingEvent = await db.OutboxEvents.SingleAsync(
                e => e.EventType == "rating.created" && e.AggregateType == "story" && e.AggregateId == storyId.ToString());
            using (var ratingPayload = JsonDocument.Parse(ratingEvent.Payload))
            {
                Assert.Equal(created.Rating.Id, ratingPayload.RootElement.GetProperty("ratingId").GetInt64());
                Assert.Equal(4, ratingPayload.RootElement.GetProperty("stars").GetInt32());
            }
        }

        var detailResponse = await fixture.Client.GetAsync($"/api/stories/{slug}");
        detailResponse.EnsureSuccessStatusCode();
        var detail = JsonSerializer.Deserialize<StoryDetailDto>(
            await detailResponse.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Equal(1, detail.RatingCount);
        Assert.Equal(4, detail.RatingAverage);

        var listResponse = await fixture.Client.GetAsync($"/api/stories/{slug}/comments");
        listResponse.EnsureSuccessStatusCode();
        var list = JsonSerializer.Deserialize<PagedResponse<CommentDto>>(
            await listResponse.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Contains(list.Items, c => c.Id == created.Comment.Id && c.Body == "What a fish!" && c.Stars == 4);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task ListPictureComments_ShowsOnlyTheStarsGivenTogetherWithTheComment()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"stars-{Guid.NewGuid():N}"[..24]);

        var textOnly = await PostCommentAsync(token, slug, "Text only");

        var starsOnlyResponse = await SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest(null, 5));
        Assert.Equal(HttpStatusCode.Created, starsOnlyResponse.StatusCode);

        var ratedResponse = await SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest("Rated", 3));
        Assert.Equal(HttpStatusCode.Created, ratedResponse.StatusCode);
        var rated = JsonSerializer.Deserialize<CommentCreatedDto>(
            await ratedResponse.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Equal((short)3, rated.Comment!.Stars);

        // A migrated anonymous comment and its rating are joined by the stored comment reference; the
        // rating carries a different timestamp than the comment.
        var legacyId = Random.Shared.Next(900_000_000, int.MaxValue);
        var legacyCreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        long legacyCommentId;
        await using (var db = fixture.CreateDbContext())
        {
            var legacyComment = new Comment
            {
                LegacyId = legacyId, StoryId = storyId, PosterName = "Guest", Body = "Legacy rated", CreatedAt = legacyCreatedAt,
            };
            db.Comments.Add(legacyComment);
            await db.SaveChangesAsync();
            db.Ratings.Add(new Rating
            {
                LegacyCommentId = legacyId, CommentId = legacyComment.Id, StoryId = storyId, Stars = 2,
                CreatedAt = legacyCreatedAt.AddHours(1),
            });
            await db.SaveChangesAsync();
            legacyCommentId = legacyComment.Id;
        }

        var listResponse = await fixture.Client.GetAsync($"/api/stories/{slug}/comments");
        listResponse.EnsureSuccessStatusCode();
        var list = JsonSerializer.Deserialize<PagedResponse<CommentDto>>(
            await listResponse.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(3, list.Items.Count);
        Assert.Null(list.Items.Single(c => c.Id == textOnly.Comment!.Id).Stars);
        Assert.Equal((short)3, list.Items.Single(c => c.Id == rated.Comment!.Id).Stars);
        Assert.Equal((short)2, list.Items.Single(c => c.Id == legacyCommentId).Stars);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostStoryComment_StarsOnlyCreatesNoComment()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"rater-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest(null, 5));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = JsonSerializer.Deserialize<CommentCreatedDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Null(created.Comment);
        Assert.NotNull(created.Rating);
        Assert.Equal(5, created.Rating!.Stars);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Comments.AnyAsync(c => c.StoryId == storyId));
        Assert.True(await db.Ratings.AnyAsync(r => r.StoryId == storyId && r.Stars == 5));
        Assert.False(await db.OutboxEvents.AnyAsync(
            e => e.EventType == "comment.created" && e.AggregateId == storyId.ToString()));

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("fine", 0)]
    [InlineData("fine", 6)]
    public async Task PostStoryComment_InvalidPayloadReturnsBadRequest(string? body, int? stars)
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"invalid-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest(body, stars));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("archived")]
    public async Task PostStoryComment_NonPublicTargetReturnsNotFound(string kind)
    {
        var slug = kind == "draft" ? fixture.Seed.DraftPictureSlug : fixture.Seed.ArchivedPictureSlug;
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"blocked-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest("hello", null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostTankComment_CreatesCommentAndTankOutboxEvent()
    {
        var tankId = await CreatePublishedTankAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"tanker-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/tanks/{tankId}/comments", new CreateCommentRequest("Nice tank!", 3));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = JsonSerializer.Deserialize<CommentCreatedDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.NotNull(created.Comment);
        Assert.NotNull(created.Rating);

        await using (var db = fixture.CreateDbContext())
        {
            var comment = await db.Comments.SingleAsync(c => c.Id == created.Comment!.Id);
            Assert.Equal(tankId, comment.TankId);
            Assert.Null(comment.StoryId);

            var rating = await db.Ratings.SingleAsync(r => r.Id == created.Rating!.Id);
            Assert.Equal(tankId, rating.TankId);

            Assert.True(await db.OutboxEvents.AnyAsync(
                e => e.EventType == "comment.created" && e.AggregateType == "tank" && e.AggregateId == tankId.ToString()));
            Assert.True(await db.OutboxEvents.AnyAsync(
                e => e.EventType == "rating.created" && e.AggregateType == "tank" && e.AggregateId == tankId.ToString()));
        }

        var listResponse = await fixture.Client.GetAsync($"/api/tanks/{tankId}/comments");
        listResponse.EnsureSuccessStatusCode();
        var list = JsonSerializer.Deserialize<PagedResponse<CommentDto>>(
            await listResponse.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Contains(list.Items, c => c.Id == created.Comment!.Id && c.Stars == 3);

        await SoftDeleteWriteTargetsAsync(tankId: tankId);
    }

    [Fact]
    public async Task PostTankComment_DraftTankReturnsNotFound()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"blocked-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(
            token, $"/api/tanks/{fixture.Seed.DraftTank.Id}/comments", new CreateCommentRequest("hello", null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostStoryComment_RepeatRatingReplacesTheRatingAndRecomputesTheAggregate()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"again-{Guid.NewGuid():N}"[..24]);

        var first = await PostCreatedAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest("First", 2));
        var second = await PostCreatedAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest("Second", 5));

        Assert.Equal(first.Rating!.Id, second.Rating!.Id);
        Assert.Equal((short)5, second.Rating.Stars);

        await using (var db = fixture.CreateDbContext())
        {
            var rating = await db.Ratings.SingleAsync(r => r.StoryId == storyId);
            Assert.Equal((short)5, rating.Stars);

            var secondComment = await db.Comments.SingleAsync(c => c.Id == second.Comment!.Id);
            Assert.Equal(secondComment.CreatedAt, rating.CreatedAt);
            Assert.Equal(secondComment.Id, rating.CommentId);

            var story = await db.Stories.SingleAsync(p => p.Id == storyId);
            Assert.Equal(1, story.RatingCount);
            Assert.Equal(5, story.RatingAverage);

            Assert.Equal(1, await db.OutboxEvents.CountAsync(
                e => e.EventType == "rating.created" && e.AggregateId == storyId.ToString()));
            var updated = await db.OutboxEvents.SingleAsync(
                e => e.EventType == "rating.updated" && e.AggregateId == storyId.ToString());
            using var payload = JsonDocument.Parse(updated.Payload);
            Assert.Equal(first.Rating.Id, payload.RootElement.GetProperty("ratingId").GetInt64());
            Assert.Equal(5, payload.RootElement.GetProperty("stars").GetInt32());
            Assert.Equal(2, payload.RootElement.GetProperty("previousStars").GetInt32());
        }

        // The stars show at the newest comment of the member, the rating row references it.
        var list = await ListCommentsAsync($"/api/stories/{slug}/comments");
        Assert.Null(list.Items.Single(c => c.Id == first.Comment!.Id).Stars);
        Assert.Equal((short)5, list.Items.Single(c => c.Id == second.Comment!.Id).Stars);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostStoryComment_RepeatRatingOfAMigratedRatingMovesTheStarsToTheNewComment()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var username = $"migrated-{Guid.NewGuid():N}"[..24];
        var token = TestTokens.Create(Guid.NewGuid().ToString(), username);
        var raterId = await EnsureProfileAsync(token, username);

        var legacyId = Random.Shared.Next(900_000_000, int.MaxValue);
        var legacyCreatedAt = DateTimeOffset.UtcNow.AddDays(-3);
        long legacyCommentId;
        await using (var db = fixture.CreateDbContext())
        {
            var legacyComment = new Comment
            {
                LegacyId = legacyId, StoryId = storyId, AuthorProfileId = raterId, Body = "Legacy", CreatedAt = legacyCreatedAt,
            };
            db.Comments.Add(legacyComment);
            await db.SaveChangesAsync();
            db.Ratings.Add(new Rating
            {
                LegacyCommentId = legacyId, CommentId = legacyComment.Id, StoryId = storyId, ProfileId = raterId, Stars = 1,
                CreatedAt = legacyCreatedAt,
            });
            await db.SaveChangesAsync();
            legacyCommentId = legacyComment.Id;
        }

        var created = await PostCreatedAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest("New", 4));

        await using (var db = fixture.CreateDbContext())
        {
            var rating = await db.Ratings.SingleAsync(r => r.StoryId == storyId);
            Assert.Equal((short)4, rating.Stars);
            Assert.Null(rating.LegacyCommentId);
            Assert.Equal(created.Comment!.Id, rating.CommentId);
        }

        var list = await ListCommentsAsync($"/api/stories/{slug}/comments");
        Assert.Null(list.Items.Single(c => c.Id == legacyCommentId).Stars);
        Assert.Equal((short)4, list.Items.Single(c => c.Id == created.Comment!.Id).Stars);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostTankComment_RepeatRatingReplacesTheRating()
    {
        var tankId = await CreatePublishedTankAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"tankagain-{Guid.NewGuid():N}"[..24]);

        var first = await PostCreatedAsync(token, $"/api/tanks/{tankId}/comments", new CreateCommentRequest(null, 1));
        var second = await PostCreatedAsync(token, $"/api/tanks/{tankId}/comments", new CreateCommentRequest(null, 4));

        Assert.Equal(first.Rating!.Id, second.Rating!.Id);

        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal((short)4, (await db.Ratings.SingleAsync(r => r.TankId == tankId)).Stars);
            Assert.True(await db.OutboxEvents.AnyAsync(
                e => e.EventType == "rating.updated" && e.AggregateType == "tank" && e.AggregateId == tankId.ToString()));
        }

        await SoftDeleteWriteTargetsAsync(tankId: tankId);
    }

    [Fact]
    public async Task PostTankComment_RatingReferencesItsCommentAndAStarsOnlyRepeatDetachesIt()
    {
        var tankId = await CreatePublishedTankAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"tankref-{Guid.NewGuid():N}"[..24]);

        var commented = await PostCreatedAsync(token, $"/api/tanks/{tankId}/comments", new CreateCommentRequest("Lovely tank", 5));

        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(commented.Comment!.Id, (await db.Ratings.SingleAsync(r => r.TankId == tankId)).CommentId);
        }

        var list = await ListCommentsAsync($"/api/tanks/{tankId}/comments");
        Assert.Equal((short)5, list.Items.Single(c => c.Id == commented.Comment!.Id).Stars);

        await PostCreatedAsync(token, $"/api/tanks/{tankId}/comments", new CreateCommentRequest(null, 2));

        await using (var db = fixture.CreateDbContext())
        {
            var rating = await db.Ratings.SingleAsync(r => r.TankId == tankId);
            Assert.Equal((short)2, rating.Stars);
            Assert.Null(rating.CommentId);
        }

        list = await ListCommentsAsync($"/api/tanks/{tankId}/comments");
        Assert.Null(list.Items.Single(c => c.Id == commented.Comment!.Id).Stars);

        await SoftDeleteWriteTargetsAsync(tankId: tankId);
    }

    [Fact]
    public async Task PostStoryComment_ParallelRepeatRatingsLeaveOneRating()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var username = $"parallel-{Guid.NewGuid():N}"[..24];
        var token = TestTokens.Create(Guid.NewGuid().ToString(), username);
        await EnsureProfileAsync(token, username);

        var responses = await Task.WhenAll(Enumerable.Range(1, 5).Select(stars =>
            SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest(null, stars))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        await using (var db = fixture.CreateDbContext())
        {
            var rating = await db.Ratings.SingleAsync(r => r.StoryId == storyId);
            var story = await db.Stories.SingleAsync(p => p.Id == storyId);
            Assert.Equal(1, story.RatingCount);
            Assert.Equal(rating.Stars, story.RatingAverage);
        }

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostStoryComment_AnonymousLegacyRatingsStayAndDoNotCountAsTheMembersRating()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        await using (var db = fixture.CreateDbContext())
        {
            db.Ratings.AddRange(
                new Rating { StoryId = storyId, Stars = 1, CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) },
                new Rating { StoryId = storyId, Stars = 1, CreatedAt = DateTimeOffset.UtcNow.AddDays(-4) });
            await db.SaveChangesAsync();
        }

        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"member-{Guid.NewGuid():N}"[..24]);
        await PostCreatedAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest(null, 5));

        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(3, await db.Ratings.CountAsync(r => r.StoryId == storyId));
            var story = await db.Stories.SingleAsync(p => p.Id == storyId);
            Assert.Equal(3, story.RatingCount);
        }

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostStoryComment_RatingOwnPictureIsForbiddenAndWritesNothing()
    {
        var username = $"owner-{Guid.NewGuid():N}"[..24];
        var token = TestTokens.Create(Guid.NewGuid().ToString(), username);
        var ownerId = await EnsureProfileAsync(token, username);
        var (storyId, slug) = await CreatePublishedPictureAsync(ownerId);

        var response = await SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest("Mine", 5));

        await AssertSelfRatingRefusedAsync(response);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.False(await db.Ratings.AnyAsync(r => r.StoryId == storyId));
            Assert.False(await db.Comments.AnyAsync(c => c.StoryId == storyId));
            Assert.False(await db.OutboxEvents.AnyAsync(e => e.AggregateType == "story" && e.AggregateId == storyId.ToString()));
        }

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostStoryComment_CommentWithoutStarsOnOwnPictureIsAllowed()
    {
        var username = $"ownertext-{Guid.NewGuid():N}"[..24];
        var token = TestTokens.Create(Guid.NewGuid().ToString(), username);
        var ownerId = await EnsureProfileAsync(token, username);
        var (storyId, slug) = await CreatePublishedPictureAsync(ownerId);

        var created = await PostCreatedAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest("Thanks all", null));

        Assert.NotNull(created.Comment);
        Assert.Null(created.Rating);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostTankComment_RatingOwnTankIsForbiddenAndWritesNothing()
    {
        var username = $"tankowner-{Guid.NewGuid():N}"[..24];
        var token = TestTokens.Create(Guid.NewGuid().ToString(), username);
        var ownerId = await EnsureProfileAsync(token, username);
        var tankId = await CreatePublishedTankAsync(ownerId);

        var response = await SendPostAsync(token, $"/api/tanks/{tankId}/comments", new CreateCommentRequest("Mine", 5));

        await AssertSelfRatingRefusedAsync(response);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.False(await db.Ratings.AnyAsync(r => r.TankId == tankId));
            Assert.False(await db.Comments.AnyAsync(c => c.TankId == tankId));
        }

        var textOnly = await PostCreatedAsync(token, $"/api/tanks/{tankId}/comments", new CreateCommentRequest("Update", null));
        Assert.NotNull(textOnly.Comment);

        await SoftDeleteWriteTargetsAsync(tankId: tankId);
    }

    [Fact]
    public async Task DeleteComment_WithoutModeratorRoleReturnsForbidden()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"plain-{Guid.NewGuid():N}"[..24]);

        var created = await PostCommentAsync(token, slug, "delete me not");

        var response = await SendDeleteAsync(token, created.Comment!.Id, new DeleteCommentRequest("spam"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task DeleteComment_ModeratorSetsTrailAndHidesFromList()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var authorToken = TestTokens.Create(Guid.NewGuid().ToString(), $"author-{Guid.NewGuid():N}"[..24]);
        var created = await PostCommentAsync(authorToken, slug, "spammy text");

        var moderatorToken = TestTokens.Create(
            Guid.NewGuid().ToString(), $"mod-{Guid.NewGuid():N}"[..24], roles: ["moderator"]);

        var response = await SendDeleteAsync(moderatorToken, created.Comment!.Id, new DeleteCommentRequest("spam"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using (var db = fixture.CreateDbContext())
        {
            var comment = await db.Comments.SingleAsync(c => c.Id == created.Comment.Id);
            Assert.NotNull(comment.DeletedAt);
            Assert.Equal("spam", comment.DeleteReason);
            Assert.NotNull(comment.DeletedByProfileId);

            var moderatorProfile = await db.Profiles.SingleAsync(p => p.Id == comment.DeletedByProfileId);
            Assert.StartsWith("mod-", moderatorProfile.Username);
        }

        var listResponse = await fixture.Client.GetAsync($"/api/stories/{slug}/comments");
        listResponse.EnsureSuccessStatusCode();
        var list = JsonSerializer.Deserialize<PagedResponse<CommentDto>>(
            await listResponse.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.DoesNotContain(list.Items, c => c.Id == created.Comment.Id);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task DeleteComment_MissingReasonReturnsBadRequest()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var authorToken = TestTokens.Create(Guid.NewGuid().ToString(), $"author-{Guid.NewGuid():N}"[..24]);
        var created = await PostCommentAsync(authorToken, slug, "still here");

        var moderatorToken = TestTokens.Create(
            Guid.NewGuid().ToString(), $"mod-{Guid.NewGuid():N}"[..24], roles: ["moderator"]);

        var response = await SendDeleteAsync(moderatorToken, created.Comment!.Id, new DeleteCommentRequest("  "));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task DeleteComment_UnknownIdReturnsNotFound()
    {
        var moderatorToken = TestTokens.Create(
            Guid.NewGuid().ToString(), $"mod-{Guid.NewGuid():N}"[..24], roles: ["moderator"]);

        var response = await SendDeleteAsync(moderatorToken, 999999999, new DeleteCommentRequest("gone"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostStoryComment_BodyOfMaximumLengthIsAccepted()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"longest-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest(new string('a', 10000), null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostStoryComment_BodyAboveMaximumLengthIsRefusedWithCode()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"toolong-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest(new string('a', 10001), 4));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "comment_too_long");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Comments.AnyAsync(c => c.StoryId == storyId));
        Assert.False(await db.Ratings.AnyAsync(r => r.StoryId == storyId));

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostStoryComment_LengthIsMeasuredAfterTrimming()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"trimmed-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(
            token, $"/api/stories/{slug}/comments", new CreateCommentRequest($"  {new string('a', 10000)}  ", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostTankComment_BodyAboveMaximumLengthIsRefusedWithCode()
    {
        var tankId = await CreatePublishedTankAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"tanklong-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/tanks/{tankId}/comments", new CreateCommentRequest(new string('a', 10001), null));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "comment_too_long");

        await SoftDeleteWriteTargetsAsync(tankId: tankId);
    }

    [Fact]
    public async Task DeleteComment_ReasonAboveMaximumLengthIsRefusedWithCode()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var authorToken = TestTokens.Create(Guid.NewGuid().ToString(), $"author-{Guid.NewGuid():N}"[..24]);
        var created = await PostCommentAsync(authorToken, slug, "still here");
        var moderatorToken = TestTokens.Create(
            Guid.NewGuid().ToString(), $"mod-{Guid.NewGuid():N}"[..24], roles: ["moderator"]);

        var tooLong = await SendDeleteAsync(moderatorToken, created.Comment!.Id, new DeleteCommentRequest(new string('r', 501)));
        await AssertProblemAsync(tooLong, HttpStatusCode.BadRequest, "reason_too_long");

        var maximum = await SendDeleteAsync(moderatorToken, created.Comment!.Id, new DeleteCommentRequest(new string('r', 500)));
        Assert.Equal(HttpStatusCode.NoContent, maximum.StatusCode);

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    [Fact]
    public async Task PostComment_JsonBodyAbove64KilobytesIsRefusedWithCode()
    {
        var (storyId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"huge-{Guid.NewGuid():N}"[..24]);
        var payload = JsonSerializer.Serialize(new { body = "hello", padding = new string('x', 70 * 1024) });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/stories/{slug}/comments")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await fixture.Client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "request_too_large");

        await SoftDeleteWriteTargetsAsync(storyId: storyId);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("detail").GetString()));
    }

    private async Task<CommentCreatedDto> PostCreatedAsync(string token, string url, CreateCommentRequest body)
    {
        var response = await SendPostAsync(token, url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonSerializer.Deserialize<CommentCreatedDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
    }

    private async Task<PagedResponse<CommentDto>> ListCommentsAsync(string url)
    {
        var response = await fixture.Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<PagedResponse<CommentDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
    }

    /// <summary>
    /// Makes the first request of a login so its profile exists, and returns the profile id.
    /// </summary>
    private async Task<long> EnsureProfileAsync(string token, string username)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await fixture.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var db = fixture.CreateDbContext();
        return (await db.Profiles.SingleAsync(p => p.Username == username)).Id;
    }

    private static async Task AssertSelfRatingRefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("self_rating", document.RootElement.GetProperty("code").GetString());
    }

    private async Task<CommentCreatedDto> PostCommentAsync(string token, string slug, string body)
    {
        var response = await SendPostAsync(token, $"/api/stories/{slug}/comments", new CreateCommentRequest(body, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonSerializer.Deserialize<CommentCreatedDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
    }

    private async Task<HttpResponseMessage> SendPostAsync(string token, string url, CreateCommentRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendDeleteAsync(string token, long commentId, DeleteCommentRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/comments/{commentId}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    /// <summary>
    /// Soft-deletes the write targets a test created, so the shared read-endpoint tests (fixed
    /// list totals, profile activity stats) never see them regardless of test order. Comments,
    /// ratings and outbox rows may stay: no read-endpoint assertion queries them beyond the
    /// shared seed's own targets.
    /// </summary>
    private async Task SoftDeleteWriteTargetsAsync(long? storyId = null, long? tankId = null)
    {
        await using var db = fixture.CreateDbContext();

        if (storyId is { } pid)
        {
            var story = await db.Stories.SingleAsync(p => p.Id == pid);
            story.DeletedAt = DateTimeOffset.UtcNow;
            story.DeleteReason = "test isolation cleanup";
        }

        if (tankId is { } tid)
        {
            var tank = await db.Tanks.SingleAsync(t => t.Id == tid);
            tank.DeletedAt = DateTimeOffset.UtcNow;
            tank.DeleteReason = "test isolation cleanup";
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a published picture story of its own for each test, so writes never disturb the
    /// shared read-endpoint seed's comment lists and rating aggregates.
    /// </summary>
    private async Task<(long StoryId, string Slug)> CreatePublishedPictureAsync(long? authorProfileId = null)
    {
        await using var db = fixture.CreateDbContext();

        var story = new Story
        {
            AuthorProfileId = authorProfileId ?? fixture.Seed.Alice.Id,
            Topic = StoryTopic.Cichlids,
            Title = "Write Target",
            State = StoryState.Published,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.Stories.Add(story);
        await db.SaveChangesAsync();

        var slug = $"write-target-{Guid.NewGuid():N}"[..28];
        db.SlugAliases.Add(new SlugAlias
        {
            StoryId = story.Id, Value = slug, IsCanonical = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return (story.Id, slug);
    }

    private async Task<long> CreatePublishedTankAsync(long? ownerProfileId = null)
    {
        await using var db = fixture.CreateDbContext();

        var tank = new Tank
        {
            ProfileId = ownerProfileId ?? fixture.Seed.Bob.Id,
            Title = "Write Target Tank",
            State = TankState.Published,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();

        return tank.Id;
    }
}
