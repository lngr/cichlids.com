// Story: TASK-9.1
using System.Net;
using System.Text.Json;
using Cichlids.Api.Tests.Features.Provisional;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// The stocking of a tank: adding groups and named fish, editing, removing with a reason, the history,
/// the owner-only writes and the visibility of the list.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-9.1")]
public sealed class InhabitantEndpointsTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Today = "2026-10-15";

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Removes the stocking, audience rows and events of the tanks of the test members, so tests that
    /// count the published tanks of the shared database see none of them.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using (var db = fixture.CreateDbContext())
        {
            var tanks = await db.Tanks.Where(t => db.Profiles.Any(p => p.Id == t.ProfileId && p.Username.StartsWith("vis-"))).Select(t => t.Id).ToListAsync();
            await db.Inhabitants.Where(i => tanks.Contains(i.TankId)).ExecuteDeleteAsync();
            await db.Follows.Where(f => f.TargetTankId != null && tanks.Contains(f.TargetTankId.Value)).ExecuteDeleteAsync();
            await db.TankSelectedMembers.Where(s => tanks.Contains(s.TankId)).ExecuteDeleteAsync();
        }

        await Streaks.StreakTestSupport.RemoveEventsAsync(fixture, "inhabitant");
    }

    private static string Url(long tank, string path = "", string date = Today) => $"/api/tanks/{tank}/inhabitants{path}?date={date}";

    private async Task<(Member Owner, long Tank)> OwnerWithTankAsync(TankVisibility profile = TankVisibility.Public)
    {
        var owner = await NewMemberAsync(fixture);
        return (owner, await NewTankAsync(fixture, owner.ProfileId, profile, profile));
    }

    private async Task<JsonElement> AddAsync(Member owner, long tank, object body, string date = Today)
    {
        using var response = await SendAsync(fixture, HttpMethod.Post, Url(tank, date: date), owner.Token, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task<JsonElement> ListAsync(long tank, string? token)
    {
        using var response = await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/inhabitants", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task<long> NewMigratedAsync(long tank, long owner, DateTimeOffset? tankCreatedAt = null)
    {
        await using var db = fixture.CreateDbContext();
        if (tankCreatedAt is { } created) await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, created));
        var species = fixture.Seed.Tropheus.Id;
        var row = new Inhabitant { TankId = tank, OwnerProfileId = owner, SpeciesId = species, Count = 5 };
        db.Inhabitants.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<List<(string Type, JsonElement Payload)>> EventsAsync(long inhabitant)
    {
        await using var db = fixture.CreateDbContext();
        var events = await db.OutboxEvents.Where(e => e.AggregateType == "inhabitant" && e.AggregateId == inhabitant.ToString()).OrderBy(e => e.OccurredAt).ToListAsync();
        return events.Select(e => (e.EventType, JsonDocument.Parse(e.Payload).RootElement.Clone())).ToList();
    }

    [Fact]
    public async Task GivenAnOwner_WhenAddingAGroupAndANamedFish_ThenTheTankPageListsBrunoFirstAndTheGroupWithTheirDays()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var species = fixture.Seed.Neolamprologus.Id;
        await AddAsync(owner, tank, new { speciesId = species, count = 12 });
        var bruno = await AddAsync(owner, tank, new { name = "Bruno", movedInOn = "2026-09-30" });

        Assert.Equal("Bruno", bruno.GetProperty("name").GetString());
        Assert.Equal(1, bruno.GetProperty("count").GetInt32());
        Assert.Equal("2026-09-30", bruno.GetProperty("movedInOn").GetString());

        using var page = await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}", owner.Token);
        var inhabitants = (await BodyAsync(page)).GetProperty("inhabitants").EnumerateArray().ToList();
        Assert.Equal(2, inhabitants.Count);
        Assert.Equal("Bruno", inhabitants[0].GetProperty("name").GetString());
        Assert.Equal("2026-09-30", inhabitants[0].GetProperty("movedInOn").GetString());
        Assert.Equal(JsonValueKind.Null, inhabitants[1].GetProperty("name").ValueKind);
        Assert.Equal(12, inhabitants[1].GetProperty("count").GetInt32());
        Assert.Equal(species, inhabitants[1].GetProperty("species").GetProperty("id").GetInt64());
        Assert.Equal(Today, inhabitants[1].GetProperty("movedInOn").GetString());
        Assert.True(inhabitants[1].GetProperty("id").GetInt64() > 0);
    }

    [Fact]
    public async Task GivenANamedFish_WhenTheOwnerRemovesItAsDied_ThenItLeavesTheStockingAndIsInTheHistoryWithDayAndReason()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var bruno = await AddAsync(owner, tank, new { name = "Bruno", movedInOn = "2026-09-01" });
        var id = bruno.GetProperty("id").GetInt64();

        using var removal = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal", "2026-10-14"), owner.Token, new { reason = "died" });
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);
        var removed = await BodyAsync(removal);
        Assert.Equal("2026-10-14", removed.GetProperty("removedOn").GetString());
        Assert.Equal("died", removed.GetProperty("removalReason").GetString());

        var list = await ListAsync(tank, owner.Token);
        Assert.Empty(list.GetProperty("current").EnumerateArray());
        var history = list.GetProperty("history").EnumerateArray().Single();
        Assert.Equal(id, history.GetProperty("id").GetInt64());
        Assert.Equal("2026-10-14", history.GetProperty("removedOn").GetString());
        Assert.Equal("died", history.GetProperty("removalReason").GetString());

        using var page = await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}", owner.Token);
        Assert.Empty((await BodyAsync(page)).GetProperty("inhabitants").EnumerateArray());
    }

    [Fact]
    public async Task GivenAMemberWhoIsNotTheOwner_WhenWritingToTheStocking_ThenTheApiAnswersLikeForAMissingTank()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var other = await NewMemberAsync(fixture);
        var existing = (await AddAsync(owner, tank, new { name = "Bruno" })).GetProperty("id").GetInt64();

        using var add = await SendAsync(fixture, HttpMethod.Post, Url(tank), other.Token, new { name = "Intruder" });
        await AssertProblemAsync(add, HttpStatusCode.NotFound, "inhabitant_not_found");
        using var update = await SendAsync(fixture, HttpMethod.Put, Url(tank, $"/{existing}"), other.Token, new { name = "Renamed" });
        await AssertProblemAsync(update, HttpStatusCode.NotFound, "inhabitant_not_found");
        using var remove = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{existing}/removal"), other.Token, new { reason = "died" });
        await AssertProblemAsync(remove, HttpStatusCode.NotFound, "inhabitant_not_found");
        using var anonymous = await SendAsync(fixture, HttpMethod.Post, Url(tank), null, new { name = "Intruder" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var list = await ListAsync(tank, owner.Token);
        var current = list.GetProperty("current").EnumerateArray().Single();
        Assert.Equal("Bruno", current.GetProperty("name").GetString());
        Assert.Empty(list.GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task GivenANameAndACountOfFive_WhenAdding_ThenTheCountIsOne()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var fish = await AddAsync(owner, tank, new { name = "  Bruno  ", count = 5, speciesId = fixture.Seed.Tropheus.Id });
        Assert.Equal("Bruno", fish.GetProperty("name").GetString());
        Assert.Equal(1, fish.GetProperty("count").GetInt32());

        using var update = await SendAsync(fixture, HttpMethod.Put, Url(tank, $"/{fish.GetProperty("id").GetInt64()}"), owner.Token, new { name = "Bruno", count = 9 });
        Assert.Equal(1, (await BodyAsync(update)).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task GivenAnInvalidInhabitant_WhenAdding_ThenItIsRefused()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        foreach (var body in new object[]
        {
            new { count = 3 },
            new { speciesId = fixture.Seed.Tropheus.Id },
            new { speciesId = fixture.Seed.Tropheus.Id, count = 0 },
            new { speciesId = fixture.Seed.Tropheus.Id, count = 10001 },
            new { name = "   " },
            new { name = new string('x', 81) },
        })
        {
            using var response = await SendAsync(fixture, HttpMethod.Post, Url(tank), owner.Token, body);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "inhabitant_invalid");
        }

        using var unknown = await SendAsync(fixture, HttpMethod.Post, Url(tank), owner.Token, new { speciesId = 999999999L, count = 2 });
        await AssertProblemAsync(unknown, HttpStatusCode.BadRequest, "inhabitant_species_unknown");
        using var unknownForNamed = await SendAsync(fixture, HttpMethod.Post, Url(tank), owner.Token, new { name = "Bruno", speciesId = 999999999L });
        await AssertProblemAsync(unknownForNamed, HttpStatusCode.BadRequest, "inhabitant_species_unknown");
        using var badDate = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{tank}/inhabitants", owner.Token, new { name = "Bruno" });
        await AssertProblemAsync(badDate, HttpStatusCode.BadRequest, "inhabitant_date_invalid");
        Assert.Empty((await ListAsync(tank, owner.Token)).GetProperty("current").EnumerateArray());
    }

    [Fact]
    public async Task GivenAMovedInDay_WhenItLiesInTheFutureOrIsMalformed_ThenItIsRefusedAndAPastDayIsKept()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        foreach (var day in new[] { "2026-10-16", "2027-01-01", "15.10.2026", "not a day" })
        {
            using var response = await SendAsync(fixture, HttpMethod.Post, Url(tank), owner.Token, new { name = "Bruno", movedInOn = day });
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "inhabitant_moved_in_invalid");
        }

        var past = await AddAsync(owner, tank, new { name = "Bruno", movedInOn = "2019-03-02" });
        Assert.Equal("2019-03-02", past.GetProperty("movedInOn").GetString());
        var sameDay = await AddAsync(owner, tank, new { name = "Emma", movedInOn = Today });
        Assert.Equal(Today, sameDay.GetProperty("movedInOn").GetString());
        var local = await AddAsync(owner, tank, new { name = "Otto" }, date: "2026-10-16");
        Assert.Equal("2026-10-16", local.GetProperty("movedInOn").GetString());

        using var update = await SendAsync(fixture, HttpMethod.Put, Url(tank, $"/{past.GetProperty("id").GetInt64()}"), owner.Token, new { name = "Bruno", movedInOn = "2026-10-16" });
        await AssertProblemAsync(update, HttpStatusCode.BadRequest, "inhabitant_moved_in_invalid");
    }

    [Fact]
    public async Task GivenACurrentInhabitant_WhenEditing_ThenSpeciesCountNameAndDayChange()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var group = await AddAsync(owner, tank, new { speciesId = fixture.Seed.Tropheus.Id, count = 4 });
        var id = group.GetProperty("id").GetInt64();

        using var edited = await SendAsync(fixture, HttpMethod.Put, Url(tank, $"/{id}"), owner.Token,
            new { speciesId = fixture.Seed.Neolamprologus.Id, count = 7, movedInOn = "2026-08-01" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var body = await BodyAsync(edited);
        Assert.Equal(7, body.GetProperty("count").GetInt32());
        Assert.Equal(fixture.Seed.Neolamprologus.Id, body.GetProperty("species").GetProperty("id").GetInt64());
        Assert.Equal("2026-08-01", body.GetProperty("movedInOn").GetString());

        using var keepDay = await SendAsync(fixture, HttpMethod.Put, Url(tank, $"/{id}"), owner.Token, new { speciesId = fixture.Seed.Neolamprologus.Id, count = 8 });
        Assert.Equal("2026-08-01", (await BodyAsync(keepDay)).GetProperty("movedInOn").GetString());

        using var missing = await SendAsync(fixture, HttpMethod.Put, Url(tank, "/999999999"), owner.Token, new { name = "Bruno" });
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "inhabitant_not_found");
    }

    [Fact]
    public async Task GivenARemoval_WhenTheDayLiesBeforeTheMoveInOrInTheFutureOrTheReasonIsUnknown_ThenItIsRefused()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var id = (await AddAsync(owner, tank, new { name = "Bruno", movedInOn = "2026-10-10" })).GetProperty("id").GetInt64();

        using var before = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "died", removedOn = "2026-10-09" });
        await AssertProblemAsync(before, HttpStatusCode.BadRequest, "inhabitant_removed_on_invalid");
        using var future = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "died", removedOn = "2026-10-16" });
        await AssertProblemAsync(future, HttpStatusCode.BadRequest, "inhabitant_removed_on_invalid");
        using var unknown = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "vanished" });
        await AssertProblemAsync(unknown, HttpStatusCode.BadRequest, "inhabitant_reason_invalid");
        using var none = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { });
        await AssertProblemAsync(none, HttpStatusCode.BadRequest, "inhabitant_reason_invalid");
        Assert.Single((await ListAsync(tank, owner.Token)).GetProperty("current").EnumerateArray());

        using var sameDay = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "given_away", removedOn = "2026-10-10" });
        Assert.Equal(HttpStatusCode.OK, sameDay.StatusCode);
        Assert.Equal("given_away", (await BodyAsync(sameDay)).GetProperty("removalReason").GetString());
    }

    [Fact]
    public async Task GivenARemovedInhabitant_WhenRemovingOrEditingItAgain_ThenTheApiAnswersWithAConflict()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var id = (await AddAsync(owner, tank, new { name = "Bruno" })).GetProperty("id").GetInt64();
        using var first = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "moved" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var again = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "died" });
        await AssertProblemAsync(again, HttpStatusCode.Conflict, "inhabitant_removed");
        using var edit = await SendAsync(fixture, HttpMethod.Put, Url(tank, $"/{id}"), owner.Token, new { name = "Renamed" });
        await AssertProblemAsync(edit, HttpStatusCode.Conflict, "inhabitant_removed");
        var history = (await ListAsync(tank, owner.Token)).GetProperty("history").EnumerateArray().Single();
        Assert.Equal("moved", history.GetProperty("removalReason").GetString());
        Assert.Equal("Bruno", history.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GivenInhabitantsOfAnotherTank_WhenWritingThroughTheTankOfTheOwner_ThenTheyAreNotFound()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var otherTank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var id = (await AddAsync(owner, otherTank, new { name = "Bruno" })).GetProperty("id").GetInt64();
        using var response = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "died" });
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "inhabitant_not_found");
    }

    [Fact]
    public async Task GivenAMigratedInhabitantWithoutADay_WhenListingAndRemoving_ThenItHasNoMovedInDayAndTheRemovalFollowsTheTankCreation()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var id = await NewMigratedAsync(tank, owner.ProfileId, new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));

        var listed = (await ListAsync(tank, owner.Token)).GetProperty("current").EnumerateArray().Single();
        Assert.Equal(id, listed.GetProperty("id").GetInt64());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("movedInOn").ValueKind);
        Assert.Equal(5, listed.GetProperty("count").GetInt32());

        using var before = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "died", removedOn = "2026-10-09" });
        await AssertProblemAsync(before, HttpStatusCode.BadRequest, "inhabitant_removed_on_invalid");
        using var after = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "died", removedOn = "2026-10-12" });
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var history = (await ListAsync(tank, owner.Token)).GetProperty("history").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, history.GetProperty("movedInOn").ValueKind);
        Assert.Equal("2026-10-12", history.GetProperty("removedOn").GetString());
    }

    [Fact]
    public async Task GivenAMigratedInhabitant_WhenEditing_ThenTheMovedInDayStaysEmptyUntilTheOwnerSetsOne()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var id = await NewMigratedAsync(tank, owner.ProfileId);
        using var edit = await SendAsync(fixture, HttpMethod.Put, Url(tank, $"/{id}"), owner.Token, new { speciesId = fixture.Seed.Tropheus.Id, count = 6 });
        Assert.Equal(JsonValueKind.Null, (await BodyAsync(edit)).GetProperty("movedInOn").ValueKind);
        using var dated = await SendAsync(fixture, HttpMethod.Put, Url(tank, $"/{id}"), owner.Token, new { speciesId = fixture.Seed.Tropheus.Id, count = 6, movedInOn = "2020-01-01" });
        Assert.Equal("2020-01-01", (await BodyAsync(dated)).GetProperty("movedInOn").GetString());
    }

    [Fact]
    public async Task GivenTheLevelsOfAProfile_WhenListing_ThenTheStockingFollowsTheProfileVisibility()
    {
        var owner = await NewMemberAsync(fixture);
        var stranger = await NewMemberAsync(fixture);
        var follower = await NewMemberAsync(fixture);
        var selected = await NewMemberAsync(fixture);
        var tanks = new Dictionary<TankVisibility, long>();
        foreach (var level in new[] { TankVisibility.Public, TankVisibility.Followers, TankVisibility.Selected, TankVisibility.Private })
        {
            tanks[level] = await NewTankAsync(fixture, owner.ProfileId, level, level);
            await AddAsync(owner, tanks[level], new { name = "Bruno" });
            await using var db = fixture.CreateDbContext();
            db.Follows.Add(new Follow { FollowerProfileId = follower.ProfileId, TargetTankId = tanks[level], Status = FollowStatus.Approved, CreatedAt = DateTimeOffset.UtcNow });
            db.TankSelectedMembers.Add(new TankSelectedMember { TankId = tanks[level], MemberProfileId = selected.ProfileId, AddedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        async Task<HttpStatusCode> StatusAsync(TankVisibility level, string? token)
        {
            using var response = await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tanks[level]}/inhabitants", token);
            if (response.StatusCode == HttpStatusCode.NotFound) await AssertProblemAsync(response, HttpStatusCode.NotFound, "inhabitant_not_found");
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(TankVisibility.Public, null));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(TankVisibility.Public, stranger.Token));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(TankVisibility.Followers, null));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(TankVisibility.Followers, stranger.Token));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(TankVisibility.Followers, follower.Token));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(TankVisibility.Selected, stranger.Token));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(TankVisibility.Selected, selected.Token));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(TankVisibility.Private, stranger.Token));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(TankVisibility.Private, follower.Token));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(TankVisibility.Private, owner.Token));
    }

    [Fact]
    public async Task GivenADraftTank_WhenAStrangerListsItsStocking_ThenItIsNotFound()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddAsync(owner, tank, new { name = "Bruno" });
        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.State, TankState.Draft));
        }

        var stranger = await NewMemberAsync(fixture);
        using var response = await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/inhabitants", stranger.Token);
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "inhabitant_not_found");
        Assert.Single((await ListAsync(tank, owner.Token)).GetProperty("current").EnumerateArray());
    }

    [Fact]
    public async Task GivenADeletedTank_WhenWriting_ThenTheApiAnswersLikeForAMissingTank()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var id = (await AddAsync(owner, tank, new { name = "Bruno" })).GetProperty("id").GetInt64();
        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, DateTimeOffset.UtcNow));
        }

        using var add = await SendAsync(fixture, HttpMethod.Post, Url(tank), owner.Token, new { name = "Emma" });
        await AssertProblemAsync(add, HttpStatusCode.NotFound, "inhabitant_not_found");
        using var remove = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "died" });
        await AssertProblemAsync(remove, HttpStatusCode.NotFound, "inhabitant_not_found");
        await using var check = fixture.CreateDbContext();
        Assert.True(await check.Inhabitants.AnyAsync(i => i.Id == id && i.RemovedOn == null));
    }

    [Fact]
    public async Task GivenAProvisionalOwner_WhenKeepingAStocking_ThenItWritesAndReadsItsOwnTank()
    {
        using var caller = await ProvisionalTestSupport.NewAsync(fixture.Client);
        var tank = await ProvisionalTestSupport.CreateTankAsync(fixture.Client, caller);

        using var added = await SendAsync(fixture, HttpMethod.Post, Url(tank), caller.Token, new { name = "Bruno" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var id = (await BodyAsync(added)).GetProperty("id").GetInt64();
        using var group = await SendAsync(fixture, HttpMethod.Post, Url(tank), caller.Token, new { speciesId = fixture.Seed.Tropheus.Id, count = 3 });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        using var removed = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), caller.Token, new { reason = "died" });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        var list = await ListAsync(tank, caller.Token);
        Assert.Single(list.GetProperty("current").EnumerateArray());
        Assert.Single(list.GetProperty("history").EnumerateArray());
        var stranger = await NewMemberAsync(fixture);
        using var hidden = await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/inhabitants", stranger.Token);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.All(await db.Inhabitants.Where(i => i.TankId == tank).ToListAsync(), i => Assert.Equal(caller.ProfileId, i.OwnerProfileId));
    }

    [Fact]
    public async Task GivenWrites_WhenTheyCommit_ThenEachEmitsAnEventWithIdsOnly()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var species = fixture.Seed.Tropheus.Id;
        var id = (await AddAsync(owner, tank, new { speciesId = species, count = 3 })).GetProperty("id").GetInt64();
        using var update = await SendAsync(fixture, HttpMethod.Put, Url(tank, $"/{id}"), owner.Token, new { speciesId = species, count = 4 });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var removal = await SendAsync(fixture, HttpMethod.Post, Url(tank, $"/{id}/removal"), owner.Token, new { reason = "given_away" });
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);

        var events = await EventsAsync(id);
        Assert.Equal(["inhabitant.added", "inhabitant.updated", "inhabitant.removed"], events.Select(e => e.Type));
        foreach (var (_, payload) in events)
        {
            Assert.Equal(["inhabitantId", "profileId", "speciesId", "tankId"], payload.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal(id, payload.GetProperty("inhabitantId").GetInt64());
            Assert.Equal(tank, payload.GetProperty("tankId").GetInt64());
            Assert.Equal(owner.ProfileId, payload.GetProperty("profileId").GetInt64());
            Assert.Equal(species, payload.GetProperty("speciesId").GetInt64());
        }
    }

    [Fact]
    public async Task GivenRefusedWrites_WhenTheyFail_ThenNoEventIsEmitted()
    {
        var (owner, tank) = await OwnerWithTankAsync();
        var other = await NewMemberAsync(fixture);
        using var refused = await SendAsync(fixture, HttpMethod.Post, Url(tank), other.Token, new { name = "Intruder" });
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        using var invalid = await SendAsync(fixture, HttpMethod.Post, Url(tank), owner.Token, new { count = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Inhabitants.Where(i => i.TankId == tank).ToListAsync());
        var events = await db.OutboxEvents.Where(e => e.AggregateType == "inhabitant").ToListAsync();
        Assert.DoesNotContain(events, e => JsonDocument.Parse(e.Payload).RootElement.GetProperty("tankId").GetInt64() == tank);
    }
}
