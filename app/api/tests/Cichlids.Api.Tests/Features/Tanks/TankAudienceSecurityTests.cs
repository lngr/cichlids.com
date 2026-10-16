using System.Net;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// The tank levels followers and selected for the owner, an approved follower, a member with an open
/// request, a selected member, another member and a caller without a login: every read path of the tank
/// profile and the care stream answers each of them from the same rule, and a history narrower than the
/// profile stays narrower.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TankAudienceSecurityTests(ApiFixture fixture) : IAsyncLifetime
{
    public enum Viewer { Owner, Follower, Requester, Selected, Other, Anonymous }

    private Member owner = null!;
    private readonly Dictionary<Viewer, Member> members = [];
    private readonly Dictionary<string, long> tanks = [];
    private readonly Dictionary<string, (long PublicEntry, long OnlyMeEntry)> entries = [];

    private static readonly (string Name, TankVisibility Profile, TankVisibility Stream)[] Setups =
    [
        ("followers_followers", TankVisibility.Followers, TankVisibility.Followers),
        ("followers_selected", TankVisibility.Followers, TankVisibility.Selected),
        ("followers_private", TankVisibility.Followers, TankVisibility.Private),
        ("selected_selected", TankVisibility.Selected, TankVisibility.Selected),
        ("public_followers", TankVisibility.Public, TankVisibility.Followers),
        ("public_selected", TankVisibility.Public, TankVisibility.Selected),
        ("public_public", TankVisibility.Public, TankVisibility.Public),
        ("private_private", TankVisibility.Private, TankVisibility.Private),
    ];

    public async Task InitializeAsync()
    {
        owner = await NewMemberAsync(fixture);
        foreach (var viewer in new[] { Viewer.Follower, Viewer.Requester, Viewer.Selected, Viewer.Other })
        {
            members[viewer] = await NewMemberAsync(fixture);
        }

        await using var db = fixture.CreateDbContext();
        foreach (var (name, profile, stream) in Setups)
        {
            var tank = await NewTankAsync(fixture, owner.ProfileId, profile, stream);
            tanks[name] = tank;
            var publicEntry = new CareEntry
            {
                TankId = tank, AuthorProfileId = owner.ProfileId, Kind = CareKind.WaterChange, OccurredAt = DateTimeOffset.UtcNow.AddDays(-3),
                RecordedAt = DateTimeOffset.UtcNow.AddDays(-3), AmountInputValue = 10m, AmountInputUnit = CareAmountUnit.Liter, AmountLiters = 10m,
            };
            var onlyMeEntry = new CareEntry
            {
                TankId = tank, AuthorProfileId = owner.ProfileId, Kind = CareKind.WaterChange, OccurredAt = DateTimeOffset.UtcNow.AddDays(-1),
                RecordedAt = DateTimeOffset.UtcNow.AddDays(-1), OnlyMe = true, AmountInputValue = 10m, AmountInputUnit = CareAmountUnit.Liter, AmountLiters = 10m,
            };
            db.CareEntries.AddRange(publicEntry, onlyMeEntry);
            await db.SaveChangesAsync();
            entries[name] = (publicEntry.Id, onlyMeEntry.Id);
            db.Follows.Add(new Follow { FollowerProfileId = members[Viewer.Follower].ProfileId, TargetTankId = tank, Status = FollowStatus.Approved, CreatedAt = DateTimeOffset.UtcNow });
            db.Follows.Add(new Follow { FollowerProfileId = members[Viewer.Requester].ProfileId, TargetTankId = tank, Status = FollowStatus.Pending, CreatedAt = DateTimeOffset.UtcNow });
            db.TankSelectedMembers.Add(new TankSelectedMember { TankId = tank, MemberProfileId = members[Viewer.Selected].ProfileId, AddedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        var ids = tanks.Values.ToList();
        await db.Comments.Where(c => c.TankId != null && ids.Contains(c.TankId.Value)).ExecuteDeleteAsync();
        await db.Follows.Where(f => f.TargetTankId != null && ids.Contains(f.TargetTankId.Value)).ExecuteDeleteAsync();
        await db.TankSelectedMembers.Where(s => ids.Contains(s.TankId)).ExecuteDeleteAsync();
        await RemoveTanksAsync(fixture, [.. ids]);
    }

    private string? TokenOf(Viewer viewer) => viewer switch
    {
        Viewer.Owner => owner.Token,
        Viewer.Anonymous => null,
        _ => members[viewer].Token,
    };

    private Task<HttpResponseMessage> GetAsync(Viewer viewer, string path) => SendAsync(fixture, HttpMethod.Get, path, TokenOf(viewer));

    // The independent oracle: who is in the audience of a level.
    private static bool InAudience(TankVisibility level, Viewer viewer) => level switch
    {
        TankVisibility.Public => true,
        TankVisibility.Followers => viewer is Viewer.Owner or Viewer.Follower or Viewer.Selected,
        TankVisibility.Selected => viewer is Viewer.Owner or Viewer.Selected,
        _ => viewer == Viewer.Owner,
    };

    private static bool SeesProfile(string setup, Viewer viewer) => InAudience(Setups.Single(s => s.Name == setup).Profile, viewer);

    private static bool SeesStream(string setup, Viewer viewer)
    {
        var (_, profile, stream) = Setups.Single(s => s.Name == setup);
        return viewer == Viewer.Owner || (InAudience(profile, viewer) && InAudience(stream, viewer));
    }

    public static TheoryData<Viewer, string> Matrix
    {
        get
        {
            var data = new TheoryData<Viewer, string>();
            foreach (var viewer in Enum.GetValues<Viewer>())
            {
                foreach (var (name, _, _) in Setups) data.Add(viewer, name);
            }

            return data;
        }
    }

    private async Task AssertProfileAsync(Viewer viewer, string setup, string path)
    {
        using var response = await GetAsync(viewer, path);
        if (SeesProfile(setup, viewer)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public Task TankDetail_FollowsTheProfileLevel(Viewer viewer, string setup) =>
        AssertProfileAsync(viewer, setup, $"/api/tanks/{tanks[setup]}");

    [Theory]
    [MemberData(nameof(Matrix))]
    public Task TankTimeLapse_FollowsTheProfileLevel(Viewer viewer, string setup) =>
        AssertProfileAsync(viewer, setup, $"/api/tanks/{tanks[setup]}/time-lapse");

    [Theory]
    [MemberData(nameof(Matrix))]
    public Task TankVideos_FollowTheProfileLevel(Viewer viewer, string setup) =>
        AssertProfileAsync(viewer, setup, $"/api/tanks/{tanks[setup]}/videos");

    [Theory]
    [MemberData(nameof(Matrix))]
    public Task TankComments_FollowTheProfileLevel(Viewer viewer, string setup) =>
        AssertProfileAsync(viewer, setup, $"/api/tanks/{tanks[setup]}/comments");

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Streaks_FollowTheProfileLevel(Viewer viewer, string setup)
    {
        using var response = await GetAsync(viewer, $"/api/tanks/{tanks[setup]}/streaks?date={DateTimeOffset.UtcNow:yyyy-MM-dd}");
        if (SeesProfile(setup, viewer)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task CommentingOnATank_NeedsAnAccountInTheAudienceOfTheProfile(Viewer viewer, string setup)
    {
        if (viewer == Viewer.Anonymous) return;
        using var response = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{tanks[setup]}/comments", TokenOf(viewer), new { body = "Nice tank" });
        if (viewer == Viewer.Owner) return;
        if (SeesProfile(setup, viewer)) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        else await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task CareEntries_FollowTheStreamLevelAndNeverShowEntriesMarkedOnlyMe(Viewer viewer, string setup)
    {
        using var response = await GetAsync(viewer, $"/api/tanks/{tanks[setup]}/care");
        if (!SeesStream(setup, viewer))
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "care_not_found");
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ids = (await BodyAsync(response)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).Order().ToList();
        var (publicEntry, onlyMeEntry) = entries[setup];
        Assert.Equal(viewer == Viewer.Owner ? new[] { publicEntry, onlyMeEntry }.Order() : [publicEntry], ids);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public Task CareSeries_FollowTheStreamLevel(Viewer viewer, string setup) =>
        AssertStreamAsync(viewer, setup, $"/api/tanks/{tanks[setup]}/care/series?parameter=ph");

    [Theory]
    [MemberData(nameof(Matrix))]
    public Task AllCareSeries_FollowTheStreamLevel(Viewer viewer, string setup) =>
        AssertStreamAsync(viewer, setup, $"/api/tanks/{tanks[setup]}/care/series/all");

    [Theory]
    [MemberData(nameof(Matrix))]
    public Task CarePlan_FollowsTheStreamLevel(Viewer viewer, string setup) =>
        AssertStreamAsync(viewer, setup, $"/api/tanks/{tanks[setup]}/care/plan?date={DateTimeOffset.UtcNow:yyyy-MM-dd}");

    [Theory]
    [MemberData(nameof(Matrix))]
    public Task CareHeatmap_FollowsTheStreamLevel(Viewer viewer, string setup) =>
        AssertStreamAsync(viewer, setup, $"/api/tanks/{tanks[setup]}/care/heatmap?date={DateTimeOffset.UtcNow:yyyy-MM-dd}");

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Decommissioning_FollowsTheStreamLevel(Viewer viewer, string setup)
    {
        using var response = await GetAsync(viewer, $"/api/tanks/{tanks[setup]}/decommissioning?date={DateTimeOffset.UtcNow:yyyy-MM-dd}");
        Assert.Equal(SeesStream(setup, viewer) ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task AssertStreamAsync(Viewer viewer, string setup, string path)
    {
        using var response = await GetAsync(viewer, path);
        if (SeesStream(setup, viewer)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else await AssertProblemAsync(response, HttpStatusCode.NotFound, "care_not_found");
    }

    [Theory]
    [InlineData(Viewer.Owner)]
    [InlineData(Viewer.Follower)]
    [InlineData(Viewer.Requester)]
    [InlineData(Viewer.Selected)]
    [InlineData(Viewer.Other)]
    [InlineData(Viewer.Anonymous)]
    public async Task TheTankListAndTheProfileCounts_ShowOnlyTheTanksTheViewerSees(Viewer viewer)
    {
        var expected = Setups.Where(s => SeesProfile(s.Name, viewer)).Select(s => tanks[s.Name]).Order().ToList();
        using var list = await GetAsync(viewer, $"/api/tanks?user={owner.ProfileId}&limit=50");
        Assert.Equal(expected, (await BodyAsync(list)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).Order().ToList());
        using var profileList = await GetAsync(viewer, $"/api/profiles/{owner.ProfileId}/tanks?limit=50");
        Assert.Equal(expected, (await BodyAsync(profileList)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).Order().ToList());
        using var profile = await GetAsync(viewer, $"/api/profiles/{owner.ProfileId}");
        Assert.Equal(expected.Count, (await BodyAsync(profile)).GetProperty("stats").GetProperty("tankCount").GetInt32());
    }

    [Fact]
    public async Task TheSettingsRefuseAHistoryWiderThanTheProfileAcrossAllFourLevels()
    {
        var levels = new[] { "private", "selected", "followers", "public" };
        var tank = tanks["followers_followers"];
        for (var profile = 0; profile < levels.Length; profile++)
        {
            for (var stream = 0; stream < levels.Length; stream++)
            {
                using var response = await SendAsync(fixture, HttpMethod.Put, $"/api/tanks/{tank}/visibility", owner.Token,
                    new { profile = levels[profile], stream = levels[stream] });
                if (stream <= profile)
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var body = await BodyAsync(response);
                    Assert.Equal(levels[profile], body.GetProperty("profile").GetString());
                    Assert.Equal(levels[stream], body.GetProperty("stream").GetString());
                }
                else
                {
                    await AssertProblemAsync(response, HttpStatusCode.BadRequest, "tank_stream_wider_than_profile");
                }
            }
        }
    }

    [Fact]
    public async Task TheDatabaseRefusesAHistoryWiderThanTheProfile()
    {
        await using var db = fixture.CreateDbContext();
        var tank = tanks["followers_followers"];
        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.tank SET profile_visibility = 'selected', stream_visibility = 'followers' WHERE id = {tank}"));
        Assert.Contains("ck_tank_stream_within_profile", error.ToString());
    }

    [Fact]
    public async Task OnlyTheOwnerChangesTheLevels()
    {
        foreach (var viewer in new[] { Viewer.Follower, Viewer.Selected, Viewer.Other })
        {
            using var response = await SendAsync(fixture, HttpMethod.Put, $"/api/tanks/{tanks["public_public"]}/visibility", TokenOf(viewer),
                new { profile = "private", stream = "private" });
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
        }
    }
}
