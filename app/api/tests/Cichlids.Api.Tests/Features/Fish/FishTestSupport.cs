using System.Net;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Fish;

/// <summary>
/// Members with named fish, files of the media library and request helpers shared by the tests of the
/// page of a named fish.
/// </summary>
internal sealed class FishWorld(ApiFixture fixture)
{
    public const string Today = "2026-10-15";

    public ApiFixture Fixture => fixture;

    public static string Url(long fish, string path = "", string date = Today) => $"/api/fish/{fish}{path}?date={date}";

    public async Task<(Member Owner, long Tank)> OwnerWithTankAsync(TankVisibility profile = TankVisibility.Public)
    {
        var owner = await NewMemberAsync(fixture);
        return (owner, await NewTankAsync(fixture, owner.ProfileId, profile, profile));
    }

    /// <summary>
    /// Adds a named fish through the stocking route and returns its id.
    /// </summary>
    public async Task<long> AddFishAsync(Member owner, long tank, string name = "Bruno", string movedInOn = "2026-09-01", long? species = null)
    {
        using var response = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{tank}/inhabitants?date={Today}", owner.Token,
            new { name, movedInOn, speciesId = species });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    public async Task<JsonElement> GetAsync(long fish, string? token, string date = Today)
    {
        using var response = await SendAsync(fixture, HttpMethod.Get, Url(fish, date: date), token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    public async Task<JsonElement> PostAsync(long fish, string path, Member owner, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendAsync(fixture, HttpMethod.Post, Url(fish, path), owner.Token, body);
        Assert.Equal(expected, response.StatusCode);
        return await BodyAsync(response);
    }

    public async Task AssertRefusedAsync(HttpMethod method, long fish, string path, string? token, object? body, HttpStatusCode status, string code)
    {
        using var response = await SendAsync(fixture, method, Url(fish, path), token, body);
        await AssertProblemAsync(response, status, code);
    }

    public static List<JsonElement> Timeline(JsonElement page) => [.. page.GetProperty("timeline").EnumerateArray()];

    public static List<string> Kinds(JsonElement page) => [.. Timeline(page).Select(i => i.GetProperty("kind").GetString()!)];

    /// <summary>
    /// Inserts a file of the member into the library without links; a photo gets a thumb variant.
    /// </summary>
    public async Task<long> NewMediaAsync(long ownerProfileId, MediaKind kind = MediaKind.Photo, DateTimeOffset? createdAt = null)
    {
        await using var db = fixture.CreateDbContext();
        var at = createdAt ?? DateTimeOffset.UtcNow;
        var item = new MediaItem
        {
            StorageKey = Guid.NewGuid().ToString(), Kind = kind, OwnerProfileId = ownerProfileId, CreatedAt = at, ReceivedAt = at,
            Width = 800, Height = 600, PlacedPublic = false,
        };
        if (kind == MediaKind.Photo) item.Variants.Add(new MediaVariant { Label = "thumb", Width = 200, StorageKey = $"private/library/{Guid.NewGuid()}" });
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    public async Task LinkAsync(Member owner, long fish, long mediaItem, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendAsync(fixture, HttpMethod.Post, Url(fish, "/media"), owner.Token, new { mediaItemId = mediaItem });
        Assert.Equal(expected, response.StatusCode);
    }

    public async Task SetMovedInAsync(long fish, DateOnly? day)
    {
        await using var db = fixture.CreateDbContext();
        await db.Inhabitants.Where(i => i.Id == fish).ExecuteUpdateAsync(s => s.SetProperty(i => i.MovedInOn, day));
    }

    public async Task SoftDeleteTankAsync(long tank)
    {
        await using var db = fixture.CreateDbContext();
        await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Removes the stocking, events, links and files of the tanks of the test members, so tests that count
    /// rows of the shared database see none of them.
    /// </summary>
    public async Task CleanAsync()
    {
        await using (var db = fixture.CreateDbContext())
        {
            var tanks = await db.Tanks.Where(t => db.Profiles.Any(p => p.Id == t.ProfileId && p.Username.StartsWith("vis-"))).Select(t => t.Id).ToListAsync();
            var members = await db.Profiles.Where(p => p.Username.StartsWith("vis-")).Select(p => p.Id).ToListAsync();
            await db.Follows.Where(f => f.TargetTankId != null && tanks.Contains(f.TargetTankId.Value)).ExecuteDeleteAsync();
            await db.TankSelectedMembers.Where(s => tanks.Contains(s.TankId)).ExecuteDeleteAsync();
            await db.MilestoneDismissals.Where(d => members.Contains(d.ProfileId)).ExecuteDeleteAsync();
            await db.Inhabitants.Where(i => tanks.Contains(i.TankId)).ExecuteDeleteAsync();
            await db.MediaItems.Where(m => m.OwnerProfileId != null && members.Contains(m.OwnerProfileId.Value)).ExecuteDeleteAsync();
        }

        await Streaks.StreakTestSupport.RemoveEventsAsync(fixture, "inhabitant");
    }
}
