using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Pictures;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Comments;

[Collection(ApiCollection.Name)]
public class CommentWriteEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task PostPictureComment_AnonymousReturnsUnauthorized()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            $"/api/pictures/{fixture.Seed.PublishedPictureACanonicalSlug}/comments",
            new CreateCommentRequest("hi", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostPictureComment_BodyAndStarsSplitIntoCommentAndRatingWithAggregatesAndOutbox()
    {
        var (postId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"writer-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/pictures/{slug}/comments", new CreateCommentRequest("What a fish!", 4));
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
            Assert.Equal(postId, comment.PostId);
            Assert.Null(comment.TankId);
            Assert.Equal(created.Comment.Author!.Id, comment.AuthorProfileId);

            var rating = await db.Ratings.SingleAsync(r => r.Id == created.Rating.Id);
            Assert.Equal(postId, rating.PostId);
            Assert.Equal((short)4, rating.Stars);
            Assert.Equal(comment.AuthorProfileId, rating.ProfileId);

            var commentEvent = await db.OutboxEvents.SingleAsync(
                e => e.EventType == "comment.created" && e.AggregateType == "post" && e.AggregateId == postId.ToString());
            using (var commentPayload = JsonDocument.Parse(commentEvent.Payload))
            {
                Assert.Equal(created.Comment.Id, commentPayload.RootElement.GetProperty("commentId").GetInt64());
                Assert.Equal(postId, commentPayload.RootElement.GetProperty("postId").GetInt64());
            }

            Assert.Null(commentEvent.DispatchedAt);

            var ratingEvent = await db.OutboxEvents.SingleAsync(
                e => e.EventType == "rating.created" && e.AggregateType == "post" && e.AggregateId == postId.ToString());
            using (var ratingPayload = JsonDocument.Parse(ratingEvent.Payload))
            {
                Assert.Equal(created.Rating.Id, ratingPayload.RootElement.GetProperty("ratingId").GetInt64());
                Assert.Equal(4, ratingPayload.RootElement.GetProperty("stars").GetInt32());
            }
        }

        var detailResponse = await fixture.Client.GetAsync($"/api/pictures/{slug}");
        detailResponse.EnsureSuccessStatusCode();
        var detail = JsonSerializer.Deserialize<PictureDetailDto>(
            await detailResponse.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Equal(1, detail.RatingCount);
        Assert.Equal(4, detail.RatingAverage);

        var listResponse = await fixture.Client.GetAsync($"/api/pictures/{slug}/comments");
        listResponse.EnsureSuccessStatusCode();
        var list = JsonSerializer.Deserialize<PagedResponse<CommentDto>>(
            await listResponse.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Contains(list.Items, c => c.Id == created.Comment.Id && c.Body == "What a fish!");

        await SoftDeleteWriteTargetsAsync(postId: postId);
    }

    [Fact]
    public async Task PostPictureComment_StarsOnlyCreatesNoComment()
    {
        var (postId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"rater-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/pictures/{slug}/comments", new CreateCommentRequest(null, 5));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = JsonSerializer.Deserialize<CommentCreatedDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Null(created.Comment);
        Assert.NotNull(created.Rating);
        Assert.Equal(5, created.Rating!.Stars);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Comments.AnyAsync(c => c.PostId == postId));
        Assert.True(await db.Ratings.AnyAsync(r => r.PostId == postId && r.Stars == 5));
        Assert.False(await db.OutboxEvents.AnyAsync(
            e => e.EventType == "comment.created" && e.AggregateId == postId.ToString()));

        await SoftDeleteWriteTargetsAsync(postId: postId);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("fine", 0)]
    [InlineData("fine", 6)]
    public async Task PostPictureComment_InvalidPayloadReturnsBadRequest(string? body, int? stars)
    {
        var (postId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"invalid-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/pictures/{slug}/comments", new CreateCommentRequest(body, stars));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await SoftDeleteWriteTargetsAsync(postId: postId);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("archived")]
    public async Task PostPictureComment_NonPublicTargetReturnsNotFound(string kind)
    {
        var slug = kind == "draft" ? fixture.Seed.DraftPictureSlug : fixture.Seed.ArchivedPictureSlug;
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"blocked-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(token, $"/api/pictures/{slug}/comments", new CreateCommentRequest("hello", null));
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
            Assert.Null(comment.PostId);

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
        Assert.Contains(list.Items, c => c.Id == created.Comment!.Id);

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
    public async Task DeleteComment_WithoutModeratorRoleReturnsForbidden()
    {
        var (postId, slug) = await CreatePublishedPictureAsync();
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"plain-{Guid.NewGuid():N}"[..24]);

        var created = await PostCommentAsync(token, slug, "delete me not");

        var response = await SendDeleteAsync(token, created.Comment!.Id, new DeleteCommentRequest("spam"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await SoftDeleteWriteTargetsAsync(postId: postId);
    }

    [Fact]
    public async Task DeleteComment_ModeratorSetsTrailAndHidesFromList()
    {
        var (postId, slug) = await CreatePublishedPictureAsync();
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

        var listResponse = await fixture.Client.GetAsync($"/api/pictures/{slug}/comments");
        listResponse.EnsureSuccessStatusCode();
        var list = JsonSerializer.Deserialize<PagedResponse<CommentDto>>(
            await listResponse.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.DoesNotContain(list.Items, c => c.Id == created.Comment.Id);

        await SoftDeleteWriteTargetsAsync(postId: postId);
    }

    [Fact]
    public async Task DeleteComment_MissingReasonReturnsBadRequest()
    {
        var (postId, slug) = await CreatePublishedPictureAsync();
        var authorToken = TestTokens.Create(Guid.NewGuid().ToString(), $"author-{Guid.NewGuid():N}"[..24]);
        var created = await PostCommentAsync(authorToken, slug, "still here");

        var moderatorToken = TestTokens.Create(
            Guid.NewGuid().ToString(), $"mod-{Guid.NewGuid():N}"[..24], roles: ["moderator"]);

        var response = await SendDeleteAsync(moderatorToken, created.Comment!.Id, new DeleteCommentRequest("  "));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await SoftDeleteWriteTargetsAsync(postId: postId);
    }

    [Fact]
    public async Task DeleteComment_UnknownIdReturnsNotFound()
    {
        var moderatorToken = TestTokens.Create(
            Guid.NewGuid().ToString(), $"mod-{Guid.NewGuid():N}"[..24], roles: ["moderator"]);

        var response = await SendDeleteAsync(moderatorToken, 999999999, new DeleteCommentRequest("gone"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<CommentCreatedDto> PostCommentAsync(string token, string slug, string body)
    {
        var response = await SendPostAsync(token, $"/api/pictures/{slug}/comments", new CreateCommentRequest(body, null));
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
    private async Task SoftDeleteWriteTargetsAsync(long? postId = null, long? tankId = null)
    {
        await using var db = fixture.CreateDbContext();

        if (postId is { } pid)
        {
            var post = await db.Posts.SingleAsync(p => p.Id == pid);
            post.DeletedAt = DateTimeOffset.UtcNow;
            post.DeleteReason = "test isolation cleanup";
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
    /// Creates a published picture post of its own for each test, so writes never disturb the
    /// shared read-endpoint seed's comment lists and rating aggregates.
    /// </summary>
    private async Task<(long PostId, string Slug)> CreatePublishedPictureAsync()
    {
        await using var db = fixture.CreateDbContext();

        var post = new Post
        {
            AuthorProfileId = fixture.Seed.Alice.Id,
            Kind = PostKind.Single,
            Topic = PostTopic.Cichlids,
            Title = "Write Target",
            State = PostState.Published,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var slug = $"write-target-{Guid.NewGuid():N}"[..28];
        db.SlugAliases.Add(new SlugAlias
        {
            PostId = post.Id, Value = slug, IsCanonical = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return (post.Id, slug);
    }

    private async Task<long> CreatePublishedTankAsync()
    {
        await using var db = fixture.CreateDbContext();

        var tank = new Tank
        {
            ProfileId = fixture.Seed.Bob.Id,
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
