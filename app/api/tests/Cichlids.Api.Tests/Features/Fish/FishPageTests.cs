// Story: TASK-9.2
using System.Net;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Fish;

/// <summary>
/// The page and timeline of a named fish: the page for the viewers of its tank, the events the owner records
/// (growth, spawning, move, death), the memorial after its death and the answers for hidden pages.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-9.2")]
public sealed class FishPageTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly FishWorld _world = new(fixture);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _world.CleanAsync();

    [Fact]
    public async Task GivenANamedFishWithPhotosAndAGrowthEntry_WhenAViewerOfItsTankOpensThePage_ThenItShowsThePhotosAndTheGrowthOnTheTimeline()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank, "Bruno", "2026-09-01", fixture.Seed.Tropheus.Id);
        var older = await _world.NewMediaAsync(owner.ProfileId, createdAt: DateTimeOffset.UtcNow.AddDays(-2));
        var newer = await _world.NewMediaAsync(owner.ProfileId, createdAt: DateTimeOffset.UtcNow.AddDays(-1));
        await _world.LinkAsync(owner, fish, older);
        await _world.LinkAsync(owner, fish, newer);
        await _world.PostAsync(fish, "/growth", owner, new { length = 7.5, unit = "cm", on = "2026-10-01" });
        var viewer = await NewMemberAsync(fixture);

        var page = await _world.GetAsync(fish, viewer.Token);

        Assert.Equal(fish, page.GetProperty("id").GetInt64());
        Assert.Equal("Bruno", page.GetProperty("name").GetString());
        Assert.Equal(fixture.Seed.Tropheus.Id, page.GetProperty("species").GetProperty("id").GetInt64());
        Assert.Equal(tank, page.GetProperty("tank").GetProperty("id").GetInt64());
        Assert.False(page.GetProperty("isOwner").GetBoolean());
        Assert.False(page.GetProperty("memorial").GetBoolean());
        Assert.Equal("2026-09-01", page.GetProperty("movedInOn").GetString());
        Assert.Equal([newer, older], page.GetProperty("photos").EnumerateArray().Select(p => p.GetProperty("mediaItemId").GetInt64()));
        Assert.StartsWith("/api/media/photos/", page.GetProperty("photos")[0].GetProperty("image").GetProperty("thumb").GetString());
        var timeline = FishWorld.Timeline(page);
        Assert.Equal(["growth", "moved_in"], FishWorld.Kinds(page));
        Assert.Equal("2026-10-01", timeline[0].GetProperty("on").GetString());
        Assert.Equal(7.5, timeline[0].GetProperty("growth").GetProperty("lengthCm").GetDouble());
        Assert.Equal("cm", timeline[0].GetProperty("growth").GetProperty("inputUnit").GetString());
        Assert.Equal("2026-09-01", timeline[1].GetProperty("on").GetString());
    }

    [Fact]
    public async Task GivenAnAnonymousViewer_WhenOpeningThePageOfAPublicTank_ThenThePageIsReadable()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);

        var page = await _world.GetAsync(fish, null);

        Assert.Equal("Bruno", page.GetProperty("name").GetString());
        Assert.False(page.GetProperty("isOwner").GetBoolean());
        Assert.True((await _world.GetAsync(fish, owner.Token)).GetProperty("isOwner").GetBoolean());
    }

    [Fact]
    public async Task GivenInchInput_WhenRecordingGrowth_ThenTheLengthIsStoredInCentimetresWithTheTypedValueAndUnit()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);

        var page = await _world.PostAsync(fish, "/growth", owner, new { length = 3.5, unit = "inch", on = "2026-10-02" });

        var growth = FishWorld.Timeline(page)[0].GetProperty("growth");
        Assert.Equal(8.89, growth.GetProperty("lengthCm").GetDouble(), 3);
        Assert.Equal(3.5, growth.GetProperty("inputValue").GetDouble());
        Assert.Equal("inch", growth.GetProperty("inputUnit").GetString());
        await using var db = fixture.CreateDbContext();
        var row = await db.InhabitantEvents.SingleAsync(e => e.InhabitantId == fish);
        Assert.Equal(InhabitantEventKind.Growth, row.Kind);
        Assert.Equal(DimensionUnit.Inch, row.LengthInputUnit);
        Assert.Equal(new DateOnly(2026, 10, 2), row.OccurredOn);
    }

    [Fact]
    public async Task GivenNoDay_WhenRecordingAnEvent_ThenItFallsOnTheLocalDateOfTheCaller()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);

        var page = await _world.PostAsync(fish, "/growth", owner, new { length = 5, unit = "cm" });

        Assert.Equal(FishWorld.Today, FishWorld.Timeline(page)[0].GetProperty("on").GetString());
    }

    [Theory]
    [InlineData(0, "cm", "2026-10-01")]
    [InlineData(-1, "cm", "2026-10-01")]
    [InlineData(301, "cm", "2026-10-01")]
    [InlineData(200, "inch", "2026-10-01")]
    [InlineData(5, "mm", "2026-10-01")]
    [InlineData(5, null, "2026-10-01")]
    public async Task GivenAnImplausibleLengthOrUnit_WhenRecordingGrowth_ThenTheApiRefuses(double length, string? unit, string on)
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);

        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/growth", owner.Token, new { length, unit, on }, HttpStatusCode.BadRequest, "fish_invalid");

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.InhabitantEvents.Where(e => e.InhabitantId == fish).ToListAsync());
    }

    [Theory]
    [InlineData("2026-10-16")]
    [InlineData("2026-08-31")]
    [InlineData("tomorrow")]
    public async Task GivenADayAfterTodayOrBeforeTheMoveIn_WhenRecordingAnEvent_ThenTheApiRefuses(string on)
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank, movedInOn: "2026-09-01");

        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/growth", owner.Token, new { length = 5, unit = "cm", on }, HttpStatusCode.BadRequest, "fish_date_invalid");
    }

    [Fact]
    public async Task GivenSpawnings_WhenRecordingThem_ThenTheOffspringIsAGenerationOrAHybrid()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);

        await _world.PostAsync(fish, "/spawning", owner, new { generation = 1, on = "2026-09-20" });
        var page = await _world.PostAsync(fish, "/spawning", owner, new { hybrid = true, on = "2026-10-01" });

        var items = FishWorld.Timeline(page);
        Assert.Equal(["spawning", "spawning", "moved_in"], FishWorld.Kinds(page));
        Assert.True(items[0].GetProperty("spawning").GetProperty("hybrid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("spawning").GetProperty("generation").ValueKind);
        Assert.False(items[1].GetProperty("spawning").GetProperty("hybrid").GetBoolean());
        Assert.Equal(1, items[1].GetProperty("spawning").GetProperty("generation").GetInt32());
    }

    [Fact]
    public async Task GivenSpawningWithoutAPurebredGenerationOrWithBoth_WhenRecordingIt_ThenTheApiRefuses()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);

        foreach (var body in new object[] { new { }, new { generation = 0 }, new { generation = 100 }, new { generation = 2, hybrid = true } })
            await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/spawning", owner.Token, body, HttpStatusCode.BadRequest, "fish_invalid");
    }

    [Fact]
    public async Task GivenANamedFishThatMovesToAnotherTankOfItsOwner_WhenTheMoveIsSaved_ThenItsPageAndTimelineStayOneAndShowTheMove()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var target = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Private, TankVisibility.Private, "Quarantine");
        var fish = await _world.AddFishAsync(owner, tank);
        var photo = await _world.NewMediaAsync(owner.ProfileId);
        await _world.LinkAsync(owner, fish, photo);
        await _world.PostAsync(fish, "/growth", owner, new { length = 6, unit = "cm", on = "2026-10-01" });

        var page = await _world.PostAsync(fish, "/move", owner, new { toTankId = target, on = "2026-10-10" });

        Assert.Equal(fish, page.GetProperty("id").GetInt64());
        Assert.Equal(target, page.GetProperty("tank").GetProperty("id").GetInt64());
        Assert.Equal(["moved", "growth", "moved_in"], FishWorld.Kinds(page));
        var move = FishWorld.Timeline(page)[0].GetProperty("move");
        Assert.Equal(tank, move.GetProperty("fromTank").GetProperty("id").GetInt64());
        Assert.Equal(target, move.GetProperty("toTank").GetProperty("id").GetInt64());
        Assert.Equal("Quarantine", move.GetProperty("toTank").GetProperty("title").GetString());
        Assert.Equal(photo, page.GetProperty("photos")[0].GetProperty("mediaItemId").GetInt64());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(target, (await db.Inhabitants.SingleAsync(i => i.Id == fish)).TankId);
        var moved = await db.InhabitantEvents.SingleAsync(e => e.InhabitantId == fish && e.Kind == InhabitantEventKind.Moved);
        Assert.Equal((tank, target), (moved.FromTankId, moved.ToTankId));
        Assert.Equal(new DateOnly(2026, 10, 10), moved.OccurredOn);
        Assert.Empty(await db.Inhabitants.Where(i => i.TankId == tank && i.RemovedOn == null).ToListAsync());
    }

    [Fact]
    public async Task GivenAMoveIntoAPrivateTank_WhenAStrangerOpensThePage_ThenThePageIsNotFound()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var target = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Private, TankVisibility.Private);
        var fish = await _world.AddFishAsync(owner, tank);
        var stranger = await NewMemberAsync(fixture);
        Assert.Equal("Bruno", (await _world.GetAsync(fish, stranger.Token)).GetProperty("name").GetString());

        await _world.PostAsync(fish, "/move", owner, new { toTankId = target });

        await _world.AssertRefusedAsync(HttpMethod.Get, fish, "", stranger.Token, null, HttpStatusCode.NotFound, "fish_not_found");
        Assert.Equal(target, (await _world.GetAsync(fish, owner.Token)).GetProperty("tank").GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task GivenAMovedFish_WhenAViewerSeesTheMoveEvent_ThenTankTitlesAppearOnlyForTanksTheViewerMaySee()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var hidden = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Private, TankVisibility.Private, "Secret quarantine");
        var fish = await _world.AddFishAsync(owner, hidden);
        await _world.PostAsync(fish, "/move", owner, new { toTankId = tank });
        var stranger = await NewMemberAsync(fixture);

        var move = FishWorld.Timeline(await _world.GetAsync(fish, stranger.Token))[0].GetProperty("move");

        Assert.Equal(tank, move.GetProperty("toTank").GetProperty("id").GetInt64());
        Assert.Equal(JsonValueKind.Null, move.GetProperty("fromTank").ValueKind);
        Assert.DoesNotContain("Secret quarantine", move.GetRawText());
    }

    [Fact]
    public async Task GivenInvalidTargets_WhenMovingAFish_ThenTheApiRefusesAndTheFishStays()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var other = await NewMemberAsync(fixture);
        var foreign = await NewTankAsync(fixture, other.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var deleted = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await _world.SoftDeleteTankAsync(deleted);
        var fish = await _world.AddFishAsync(owner, tank);

        foreach (var toTankId in new long[] { foreign, deleted, tank, long.MaxValue })
            await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/move", owner.Token, new { toTankId }, HttpStatusCode.BadRequest, "fish_move_tank_invalid");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/move", owner.Token, new { }, HttpStatusCode.BadRequest, "fish_move_tank_invalid");

        await using var db = fixture.CreateDbContext();
        Assert.Equal(tank, (await db.Inhabitants.SingleAsync(i => i.Id == fish)).TankId);
        Assert.Empty(await db.InhabitantEvents.Where(e => e.InhabitantId == fish).ToListAsync());
    }

    [Fact]
    public async Task GivenANamedFish_WhenTheOwnerRecordsItsDeath_ThenItLeavesTheStockingAndShowsAsAMemorialWithItsDates()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank, movedInOn: "2026-09-01");
        var viewer = await NewMemberAsync(fixture);

        await _world.PostAsync(fish, "/death", owner, new { on = "2026-10-10" });

        var page = await _world.GetAsync(fish, viewer.Token);
        Assert.True(page.GetProperty("memorial").GetBoolean());
        Assert.Equal("2026-09-01", page.GetProperty("movedInOn").GetString());
        Assert.Equal("2026-10-10", page.GetProperty("removedOn").GetString());
        Assert.Equal("died", page.GetProperty("removalReason").GetString());
        Assert.Equal(["died", "moved_in"], FishWorld.Kinds(page));
        Assert.Equal(tank, page.GetProperty("tank").GetProperty("id").GetInt64());
        await using var db = fixture.CreateDbContext();
        var row = await db.Inhabitants.SingleAsync(i => i.Id == fish);
        Assert.Equal((new DateOnly(2026, 10, 10), InhabitantRemovalReason.Died), (row.RemovedOn, row.RemovalReason));
        Assert.Single(await db.InhabitantEvents.Where(e => e.InhabitantId == fish && e.Kind == InhabitantEventKind.Died).ToListAsync());
        using var list = await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/inhabitants", owner.Token);
        Assert.Empty((await BodyAsync(list)).GetProperty("current").EnumerateArray());
        Assert.Single((await BodyAsync(list)).GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task GivenANamedFishRemovedAsDiedFromTheStocking_WhenOpeningItsPage_ThenTheTimelineShowsTheDeath()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank, movedInOn: "2026-09-01");

        using var removal = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{tank}/inhabitants/{fish}/removal?date={FishWorld.Today}", owner.Token,
            new { reason = "died", removedOn = "2026-10-12" });
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);

        var page = await _world.GetAsync(fish, owner.Token);
        Assert.True(page.GetProperty("memorial").GetBoolean());
        Assert.Equal(["died", "moved_in"], FishWorld.Kinds(page));
        Assert.Equal("2026-10-12", FishWorld.Timeline(page)[0].GetProperty("on").GetString());
    }

    [Fact]
    public async Task GivenAFishGivenAway_WhenOpeningItsPage_ThenItIsNoMemorialAndTheTimelineShowsTheDeparture()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank, movedInOn: "2026-09-01");
        using var removal = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{tank}/inhabitants/{fish}/removal?date={FishWorld.Today}", owner.Token,
            new { reason = "given_away", removedOn = "2026-10-05" });
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);

        var page = await _world.GetAsync(fish, owner.Token);

        Assert.False(page.GetProperty("memorial").GetBoolean());
        Assert.Equal(["left", "moved_in"], FishWorld.Kinds(page));
        Assert.Equal("given_away", FishWorld.Timeline(page)[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task GivenADeadFish_WhenTheOwnerWritesAnEventOrDiesAgain_ThenTheApiRefuses()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var target = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var fish = await _world.AddFishAsync(owner, tank);
        await _world.PostAsync(fish, "/death", owner, new { });

        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/death", owner.Token, new { }, HttpStatusCode.Conflict, "fish_removed");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/growth", owner.Token, new { length = 5, unit = "cm" }, HttpStatusCode.Conflict, "fish_removed");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/spawning", owner.Token, new { generation = 1 }, HttpStatusCode.Conflict, "fish_removed");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/move", owner.Token, new { toTankId = target }, HttpStatusCode.Conflict, "fish_removed");
    }

    [Theory]
    [InlineData(TankVisibility.Public, "anonymous", true)]
    [InlineData(TankVisibility.Public, "member", true)]
    [InlineData(TankVisibility.Followers, "anonymous", false)]
    [InlineData(TankVisibility.Followers, "member", false)]
    [InlineData(TankVisibility.Followers, "follower", true)]
    [InlineData(TankVisibility.Followers, "selected", true)]
    [InlineData(TankVisibility.Selected, "follower", false)]
    [InlineData(TankVisibility.Selected, "selected", true)]
    [InlineData(TankVisibility.Private, "selected", false)]
    [InlineData(TankVisibility.Private, "owner", true)]
    public async Task GivenTheProfileLevelOfTheTank_WhenAViewerOpensThePage_ThenItIsVisibleExactlyToTheAudienceOfTheLevel(
        TankVisibility level, string who, bool visible)
    {
        var (owner, tank) = await _world.OwnerWithTankAsync(level);
        var fish = await _world.AddFishAsync(owner, tank);
        var viewer = await NewMemberAsync(fixture);
        await using (var db = fixture.CreateDbContext())
        {
            if (who == "follower")
                db.Follows.Add(new Follow { FollowerProfileId = viewer.ProfileId, TargetTankId = tank, Status = FollowStatus.Approved, CreatedAt = DateTimeOffset.UtcNow, DecidedAt = DateTimeOffset.UtcNow });
            if (who == "selected") db.TankSelectedMembers.Add(new TankSelectedMember { TankId = tank, MemberProfileId = viewer.ProfileId, AddedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var token = who switch { "anonymous" => null, "owner" => owner.Token, _ => viewer.Token };
        using var response = await SendAsync(fixture, HttpMethod.Get, FishWorld.Url(fish), token);

        if (visible) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else await AssertProblemAsync(response, HttpStatusCode.NotFound, "fish_not_found");
    }

    [Fact]
    public async Task GivenAHiddenPage_WhenComparedWithAMissingFish_ThenTheAnswersAreTheSame()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync(TankVisibility.Private);
        var fish = await _world.AddFishAsync(owner, tank);
        var group = (await AddGroupAsync(owner, tank));
        var stranger = await NewMemberAsync(fixture);

        using var hidden = await SendAsync(fixture, HttpMethod.Get, FishWorld.Url(fish), stranger.Token);
        using var missing = await SendAsync(fixture, HttpMethod.Get, FishWorld.Url(long.MaxValue), stranger.Token);
        using var notAFish = await SendAsync(fixture, HttpMethod.Get, FishWorld.Url(group), owner.Token);

        foreach (var response in new[] { hidden, missing, notAFish })
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "fish_not_found");
        var hiddenBody = await BodyAsync(hidden);
        var missingBody = await BodyAsync(missing);
        foreach (var member in new[] { "title", "detail", "status", "code" })
            Assert.Equal(missingBody.GetProperty(member).ToString(), hiddenBody.GetProperty(member).ToString());
    }

    private async Task<long> AddGroupAsync(Member owner, long tank)
    {
        using var response = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{tank}/inhabitants?date={FishWorld.Today}", owner.Token,
            new { speciesId = fixture.Seed.Tropheus.Id, count = 4 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    [Fact]
    public async Task GivenAFishThatDied_WhenTheTankBecomesPrivate_ThenTheMemorialFollowsTheLastTank()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        await _world.PostAsync(fish, "/death", owner, new { });
        var viewer = await NewMemberAsync(fixture);
        Assert.True((await _world.GetAsync(fish, viewer.Token)).GetProperty("memorial").GetBoolean());

        await using (var db = fixture.CreateDbContext())
            await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ProfileVisibility, TankVisibility.Private).SetProperty(t => t.StreamVisibility, TankVisibility.Private));

        await _world.AssertRefusedAsync(HttpMethod.Get, fish, "", viewer.Token, null, HttpStatusCode.NotFound, "fish_not_found");
        Assert.True((await _world.GetAsync(fish, owner.Token)).GetProperty("memorial").GetBoolean());
    }

    [Fact]
    public async Task GivenADeletedTank_WhenTheOwnerOpensThePage_ThenThePageAndItsTimelineRemainAndOthersGetNotFound()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        await _world.PostAsync(fish, "/growth", owner, new { length = 5, unit = "cm", on = "2026-10-01" });
        var viewer = await NewMemberAsync(fixture);
        await _world.SoftDeleteTankAsync(tank);

        var page = await _world.GetAsync(fish, owner.Token);

        Assert.Equal(["growth", "moved_in"], FishWorld.Kinds(page));
        Assert.Equal(tank, page.GetProperty("tank").GetProperty("id").GetInt64());
        await _world.AssertRefusedAsync(HttpMethod.Get, fish, "", viewer.Token, null, HttpStatusCode.NotFound, "fish_not_found");
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Inhabitants.AnyAsync(i => i.Id == fish));
    }

    [Fact]
    public async Task GivenAMemberWhoIsNotTheOwner_WhenWritingToTheFish_ThenTheApiAnswersNotFoundAndChangesNothing()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        var intruder = await NewMemberAsync(fixture);
        var theirTank = await NewTankAsync(fixture, intruder.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var photo = await _world.NewMediaAsync(intruder.ProfileId);
        var growth = await _world.PostAsync(fish, "/growth", owner, new { length = 5, unit = "cm" });
        var eventId = FishWorld.Timeline(growth)[0].GetProperty("eventId").GetInt64();

        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/growth", intruder.Token, new { length = 9, unit = "cm" }, HttpStatusCode.NotFound, "fish_not_found");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/spawning", intruder.Token, new { generation = 1 }, HttpStatusCode.NotFound, "fish_not_found");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/move", intruder.Token, new { toTankId = theirTank }, HttpStatusCode.NotFound, "fish_not_found");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/death", intruder.Token, new { }, HttpStatusCode.NotFound, "fish_not_found");
        await _world.AssertRefusedAsync(HttpMethod.Delete, fish, $"/events/{eventId}", intruder.Token, null, HttpStatusCode.NotFound, "fish_not_found");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/media", intruder.Token, new { mediaItemId = photo }, HttpStatusCode.NotFound, "fish_not_found");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/growth", null, new { length = 9, unit = "cm" }, HttpStatusCode.Unauthorized, "fish_auth_required");

        await using var db = fixture.CreateDbContext();
        var row = await db.Inhabitants.SingleAsync(i => i.Id == fish);
        Assert.Equal((tank, null), (row.TankId, row.RemovedOn));
        Assert.Single(await db.InhabitantEvents.Where(e => e.InhabitantId == fish && e.Kind == InhabitantEventKind.Growth).ToListAsync());
        Assert.Empty(await db.MediaLinks.Where(l => l.InhabitantId == fish).ToListAsync());
    }

    [Fact]
    public async Task GivenGrowthAndSpawning_WhenTheOwnerDeletesThem_ThenTheyLeaveTheTimelineButMovesAndDeathStay()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var target = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var fish = await _world.AddFishAsync(owner, tank);
        await _world.PostAsync(fish, "/growth", owner, new { length = 5, unit = "cm", on = "2026-10-01" });
        await _world.PostAsync(fish, "/spawning", owner, new { generation = 1, on = "2026-10-02" });
        var moved = await _world.PostAsync(fish, "/move", owner, new { toTankId = target, on = "2026-10-03" });
        var items = FishWorld.Timeline(moved);
        var moveId = items[0].GetProperty("eventId").GetInt64();

        await _world.AssertRefusedAsync(HttpMethod.Delete, fish, $"/events/{moveId}", owner.Token, null, HttpStatusCode.Conflict, "fish_event_fixed");
        foreach (var item in items.Where(i => i.GetProperty("kind").GetString() is "growth" or "spawning"))
        {
            using var response = await SendAsync(fixture, HttpMethod.Delete, FishWorld.Url(fish, $"/events/{item.GetProperty("eventId").GetInt64()}"), owner.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(["moved", "moved_in"], FishWorld.Kinds(await _world.GetAsync(fish, owner.Token)));
        await _world.AssertRefusedAsync(HttpMethod.Delete, fish, "/events/999999999", owner.Token, null, HttpStatusCode.NotFound, "fish_not_found");
    }

    [Fact]
    public async Task GivenAMovedInDay_WhenOpeningThePage_ThenTheCompletedYearsAppearAsAnniversariesWithoutBeingStored()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank, movedInOn: "2026-09-01");
        await _world.SetMovedInAsync(fish, new DateOnly(2023, 10, 20));

        var page = await _world.GetAsync(fish, owner.Token);

        Assert.Equal(["anniversary", "anniversary", "moved_in"], FishWorld.Kinds(page).Where(k => k != "growth").ToList());
        var anniversaries = FishWorld.Timeline(page).Where(i => i.GetProperty("kind").GetString() == "anniversary").ToList();
        Assert.Equal([2, 1], anniversaries.Select(a => a.GetProperty("year").GetInt32()));
        Assert.Equal(["2025-10-20", "2024-10-20"], anniversaries.Select(a => a.GetProperty("on").GetString()));
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.InhabitantEvents.Where(e => e.InhabitantId == fish).ToListAsync());
    }

    [Fact]
    public async Task GivenAFishThatDiedBeforeItsNextAnniversary_WhenOpeningThePage_ThenNoAnniversaryFollowsTheDeath()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank, movedInOn: "2026-09-01");
        await _world.SetMovedInAsync(fish, new DateOnly(2023, 10, 20));
        await _world.PostAsync(fish, "/death", owner, new { on = "2025-03-01" });

        var kinds = FishWorld.Kinds(await _world.GetAsync(fish, owner.Token));

        Assert.Equal(["died", "anniversary", "moved_in"], kinds);
    }

    [Fact]
    public async Task GivenAMigratedFishWithoutAMovedInDay_WhenOpeningThePage_ThenTheTimelineHasNoMoveInAndNoAnniversary()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        await _world.SetMovedInAsync(fish, null);

        var page = await _world.GetAsync(fish, owner.Token);

        Assert.Empty(FishWorld.Timeline(page));
        Assert.Equal(JsonValueKind.Null, page.GetProperty("movedInOn").ValueKind);
    }

    [Fact]
    public async Task GivenEventsOnTheSameDay_WhenOpeningThePage_ThenTheTimelineIsNewestFirstWithTheMoveInLast()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank, movedInOn: "2026-10-01");
        await _world.PostAsync(fish, "/growth", owner, new { length = 5, unit = "cm", on = "2026-10-01" });
        await _world.PostAsync(fish, "/death", owner, new { on = "2026-10-01" });

        Assert.Equal(["died", "growth", "moved_in"], FishWorld.Kinds(await _world.GetAsync(fish, owner.Token)));
    }

    [Fact]
    public async Task GivenAProvisionalOwner_WhenKeepingAFish_ThenItWritesEventsAndOnlyItReadsThePage()
    {
        using var caller = await Provisional.ProvisionalTestSupport.NewAsync(fixture.Client);
        var tank = await Provisional.ProvisionalTestSupport.CreateTankAsync(fixture.Client, caller);
        var second = await Provisional.ProvisionalTestSupport.CreateTankAsync(fixture.Client, caller);
        using var added = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{tank}/inhabitants?date={FishWorld.Today}", caller.Token, new { name = "Bruno" });
        var fish = (await BodyAsync(added)).GetProperty("id").GetInt64();

        using var growth = await SendAsync(fixture, HttpMethod.Post, FishWorld.Url(fish, "/growth"), caller.Token, new { length = 4, unit = "cm" });
        using var move = await SendAsync(fixture, HttpMethod.Post, FishWorld.Url(fish, "/move"), caller.Token, new { toTankId = second });

        Assert.Equal(HttpStatusCode.OK, growth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        var page = await _world.GetAsync(fish, caller.Token);
        Assert.Equal(["moved", "growth", "moved_in"], FishWorld.Kinds(page));
        Assert.True(page.GetProperty("isOwner").GetBoolean());
        var stranger = await NewMemberAsync(fixture);
        await _world.AssertRefusedAsync(HttpMethod.Get, fish, "", stranger.Token, null, HttpStatusCode.NotFound, "fish_not_found");
        await _world.AssertRefusedAsync(HttpMethod.Get, fish, "", null, null, HttpStatusCode.NotFound, "fish_not_found");
    }

    [Fact]
    public async Task GivenWrites_WhenTheyCommit_ThenEachEmitsAnEventWithIdsOnly()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var target = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var fish = await _world.AddFishAsync(owner, tank);
        await _world.PostAsync(fish, "/growth", owner, new { length = 5, unit = "cm" });
        await _world.PostAsync(fish, "/spawning", owner, new { generation = 1 });
        await _world.PostAsync(fish, "/move", owner, new { toTankId = target });
        await _world.PostAsync(fish, "/death", owner, new { });

        await using var db = fixture.CreateDbContext();
        var events = (await db.OutboxEvents.Where(e => e.AggregateType == "inhabitant" && e.AggregateId == fish.ToString()).OrderBy(e => e.OccurredAt).ToListAsync())
            .Select(e => (e.EventType, Payload: JsonDocument.Parse(e.Payload).RootElement)).ToList();
        Assert.Equal(["inhabitant.added", "inhabitant.growth_recorded", "inhabitant.spawned", "inhabitant.moved", "inhabitant.removed"], events.Select(e => e.EventType));
        foreach (var (_, payload) in events.Skip(1))
        {
            Assert.Equal(["inhabitantId", "profileId", "speciesId", "tankId"], payload.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal(fish, payload.GetProperty("inhabitantId").GetInt64());
        }

        Assert.Equal(target, events[3].Payload.GetProperty("tankId").GetInt64());
    }
}
