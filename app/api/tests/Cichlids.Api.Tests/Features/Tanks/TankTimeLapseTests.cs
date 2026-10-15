using System.Net;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Tanks;

[Collection("TankTimeLapse")]
public class TankTimeLapseTests(ApiFixture fixture)
{
    [Theory]
    [InlineData(8, 12, true)]
    [InlineData(7, 12, false)]
    [InlineData(8, 11, false)]
    [InlineData(2, 12, false)]
    [InlineData(8, 12, false, true)]
    public async Task Series_UsesDistinctMonthsAndCalendarYear(int count, int spanMonths, bool expected, bool sameMonth = false)
    {
        await using var db = fixture.CreateDbContext();
        var tank = new Tank { ProfileId = fixture.Seed.Alice.Id, Title = "Series", State = TankState.Published };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        var start = new DateTimeOffset(2020, 2, 29, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < count; i++)
        {
            var media = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, CreatedAt = start.AddMonths(i == count - 1 ? spanMonths : sameMonth ? 0 : i) };
            db.MediaItems.Add(media);
            await db.SaveChangesAsync();
            db.TankMedia.Add(new TankMedia { TankId = tank.Id, MediaItemId = media.Id, Sort = count - i });
            db.MediaVariants.Add(new MediaVariant { MediaItemId = media.Id, Label = "large", StorageKey = $"variants/large/{media.Id}.jpg" });
        }
        await db.SaveChangesAsync();
        var response = await fixture.Client.GetAsync($"/api/tanks/{tank.Id}/time-lapse");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement;
        Assert.Equal(expected, body.GetProperty("isShowcase").GetBoolean());
        var photos = body.GetProperty("photos").EnumerateArray().ToArray();
        Assert.Equal(count, photos.Length);
        Assert.Equal(start, photos[0].GetProperty("publishedAt").GetDateTimeOffset());
        Assert.All(photos, p => Assert.Contains("variants/large/", p.GetProperty("image").GetProperty("large").GetString()));
        Assert.All(photos, p => Assert.Equal(JsonValueKind.Null, p.GetProperty("takenAt").ValueKind));
        if (expected)
        {
            var owner = await db.Profiles.FindAsync(fixture.Seed.Alice.Id);
            owner!.Kind = ProfileKind.Archived;
            await db.SaveChangesAsync();
            try
            {
                using var archived = JsonDocument.Parse(await fixture.Client.GetStringAsync($"/api/tanks/{tank.Id}/time-lapse"));
                Assert.False(archived.RootElement.GetProperty("isShowcase").GetBoolean());
            }
            finally
            {
                owner.Kind = ProfileKind.Member;
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Series_DeduplicatesPhotosIncludesPublishedPostsAndExcludesVideoDraftsAndSuspendedOwners()
    {
        await using var db = fixture.CreateDbContext();
        var tank = new Tank { ProfileId = fixture.Seed.Alice.Id, Title = "Mixed series", State = TankState.Published };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        var date = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        var photo = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, CreatedAt = date };
        var video = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Video, CreatedAt = date };
        db.MediaItems.AddRange(photo, video);
        await db.SaveChangesAsync();
        db.TankMedia.Add(new TankMedia { TankId = tank.Id, MediaItemId = photo.Id });
        db.TankMedia.Add(new TankMedia { TankId = tank.Id, MediaItemId = video.Id });
        db.MediaVariants.Add(new MediaVariant { MediaItemId = photo.Id, Label = "large", StorageKey = "variants/large/series.jpg" });
        foreach (var state in new[] { PostState.Published, PostState.Draft })
        {
            var post = new Post { TankId = tank.Id, AuthorProfileId = fixture.Seed.Alice.Id, Title = state.ToString(), State = state, PublishedAt = date.AddYears(1) };
            db.Posts.Add(post);
            await db.SaveChangesAsync();
            db.PostMedia.Add(new PostMedia { PostId = post.Id, MediaItemId = photo.Id });
        }
        await db.SaveChangesAsync();
        using var response = await fixture.Client.GetAsync($"/api/tanks/{tank.Id}/time-lapse");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(json.RootElement.GetProperty("photos").EnumerateArray());
        Assert.Single(json.RootElement.GetProperty("posts").EnumerateArray());
        var alice = await db.Profiles.FindAsync(fixture.Seed.Alice.Id);
        alice!.SuspendedUntil = DateTimeOffset.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.GetAsync($"/api/tanks/{tank.Id}/time-lapse")).StatusCode);
        }
        finally
        {
            alice.SuspendedUntil = null;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task OpenApi_DeclaresUnavailableTankAsProblemDetails()
    {
        using var json = JsonDocument.Parse(await fixture.Client.GetStringAsync("/openapi/v1.json"));
        var responses = json.RootElement.GetProperty("paths").GetProperty("/api/tanks/{id}/time-lapse")
            .GetProperty("get").GetProperty("responses");
        Assert.True(responses.TryGetProperty("404", out var problem));
        Assert.True(problem.GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task MissingTank_ReturnsCodedProblem(long id)
    {
        var response = await fixture.Client.GetAsync($"/api/tanks/{id}/time-lapse");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("tank_not_found", json.RootElement.GetProperty("code").GetString());
    }
}

[CollectionDefinition("TankTimeLapse")]
public class TankTimeLapseCollection : ICollectionFixture<ApiFixture>;
