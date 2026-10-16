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
            db.MediaLinks.Add(MediaLink.ForTank(tank.Id, media.Id, MediaLinkRole.Showcase, count - i));
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
    public async Task Series_OrdersByCaptureTimeAndFallsBackToTheUploadDatePerPhoto()
    {
        await using var db = fixture.CreateDbContext();
        var tank = new Tank { ProfileId = fixture.Seed.Alice.Id, Title = "Captured series", State = TankState.Published };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        var uploadedLate = Add(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero), new DateTime(2019, 3, 1, 9, 0, 0), null);
        var uploadedEarly = Add(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), null, null);
        var nearYearEnd = Add(new DateTimeOffset(2025, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTime(2023, 12, 31, 23, 30, 0), 120);
        var unknownZone = Add(new DateTimeOffset(2025, 9, 2, 0, 0, 0, TimeSpan.Zero), new DateTime(2022, 7, 4, 10, 0, 0), null);
        await db.SaveChangesAsync();
        foreach (var media in new[] { uploadedLate, uploadedEarly, nearYearEnd, unknownZone })
        {
            db.MediaLinks.Add(MediaLink.ForTank(tank.Id, media.Id, MediaLinkRole.Showcase));
            db.MediaVariants.Add(new MediaVariant { MediaItemId = media.Id, Label = "large", StorageKey = $"variants/large/{media.Id}.jpg" });
        }
        await db.SaveChangesAsync();

        using var json = JsonDocument.Parse(await fixture.Client.GetStringAsync($"/api/tanks/{tank.Id}/time-lapse"));

        var photos = json.RootElement.GetProperty("photos").EnumerateArray().ToArray();
        Assert.Equal([uploadedLate.Id, uploadedEarly.Id, unknownZone.Id, nearYearEnd.Id], photos.Select(p => p.GetProperty("mediaItemId").GetInt64()));
        Assert.Equal(JsonValueKind.Null, photos[1].GetProperty("takenAt").ValueKind);
        Assert.Equal(new DateTimeOffset(2019, 3, 1, 9, 0, 0, TimeSpan.Zero), photos[0].GetProperty("takenAt").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(2023, 12, 31, 23, 30, 0, TimeSpan.FromHours(2)), photos[3].GetProperty("takenAt").GetDateTimeOffset());
        Assert.Equal(TimeSpan.FromHours(2), photos[3].GetProperty("takenAt").GetDateTimeOffset().Offset);

        MediaItem Add(DateTimeOffset uploaded, DateTime? taken, short? offsetMinutes)
        {
            var media = new MediaItem
            {
                StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, CreatedAt = uploaded,
                TakenAtLocal = taken, TakenAtOffsetMinutes = offsetMinutes,
            };
            db.MediaItems.Add(media);
            return media;
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Series_AppliesTheShowcaseRuleToCaptureTimesInsteadOfUploadDates(bool capturedAcrossMonths, bool expected)
    {
        await using var db = fixture.CreateDbContext();
        var tank = new Tank { ProfileId = fixture.Seed.Alice.Id, Title = "Showcase by capture", State = TankState.Published };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        for (var i = 0; i < 8; i++)
        {
            // Eight photos uploaded within a single month; the capture times decide.
            var media = new MediaItem
            {
                StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo,
                CreatedAt = new DateTimeOffset(2026, 1, 1 + i, 0, 0, 0, TimeSpan.Zero),
                TakenAtLocal = capturedAcrossMonths
                    ? new DateTime(2022, 1, 15).AddMonths(i == 7 ? 14 : i)
                    : new DateTime(2022, 1, 5).AddDays(i),
            };
            db.MediaItems.Add(media);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForTank(tank.Id, media.Id, MediaLinkRole.Showcase));
            db.MediaVariants.Add(new MediaVariant { MediaItemId = media.Id, Label = "large", StorageKey = $"variants/large/{media.Id}.jpg" });
        }
        await db.SaveChangesAsync();

        using var json = JsonDocument.Parse(await fixture.Client.GetStringAsync($"/api/tanks/{tank.Id}/time-lapse"));

        Assert.Equal(expected, json.RootElement.GetProperty("isShowcase").GetBoolean());
    }

    [Fact]
    public async Task Series_ReportsNoShowcaseWhenUploadsSpanMonthsButCaptureTimesDoNot()
    {
        await using var db = fixture.CreateDbContext();
        var tank = new Tank { ProfileId = fixture.Seed.Alice.Id, Title = "Uploaded across months", State = TankState.Published };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        for (var i = 0; i < 8; i++)
        {
            var media = new MediaItem
            {
                StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo,
                CreatedAt = new DateTimeOffset(2020, 1, 15, 0, 0, 0, TimeSpan.Zero).AddMonths(i == 7 ? 14 : i),
                TakenAtLocal = new DateTime(2019, 5, 1 + i),
            };
            db.MediaItems.Add(media);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForTank(tank.Id, media.Id, MediaLinkRole.Showcase));
            db.MediaVariants.Add(new MediaVariant { MediaItemId = media.Id, Label = "large", StorageKey = $"variants/large/{media.Id}.jpg" });
        }
        await db.SaveChangesAsync();

        using var json = JsonDocument.Parse(await fixture.Client.GetStringAsync($"/api/tanks/{tank.Id}/time-lapse"));

        Assert.False(json.RootElement.GetProperty("isShowcase").GetBoolean());
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
        db.MediaLinks.Add(MediaLink.ForTank(tank.Id, photo.Id, MediaLinkRole.Showcase));
        db.MediaLinks.Add(MediaLink.ForTank(tank.Id, video.Id, MediaLinkRole.Showcase));
        db.MediaVariants.Add(new MediaVariant { MediaItemId = photo.Id, Label = "large", StorageKey = "variants/large/series.jpg" });
        foreach (var state in new[] { PostState.Published, PostState.Draft })
        {
            var post = new Post { TankId = tank.Id, AuthorProfileId = fixture.Seed.Alice.Id, Title = state.ToString(), State = state, PublishedAt = date.AddYears(1) };
            db.Posts.Add(post);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForPost(post.Id, photo.Id));
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
