using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Features.Me;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Provisional.ProvisionalTestSupport;
using static Cichlids.Api.Tests.Features.Streaks.StreakTestSupport;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Me;

/// <summary>
/// The return retrospective: when it is due, which cards it holds, what a member may see in it and
/// how seeing it is stored.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-8.4")]
public sealed class ReturnRetrospectiveTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Route = "/api/me/retrospectives/return";
    private readonly List<long> _stories = [];
    private readonly List<long> _tanks = [];
    private readonly List<long> _comments = [];
    private readonly List<long> _mediaItems = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.CommentVotes.Where(v => _comments.Contains(v.CommentId)).ExecuteDeleteAsync();
        await db.Comments.Where(c => _comments.Contains(c.Id)).ExecuteDeleteAsync();
        await db.Stories.Where(s => _stories.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Follows.Where(f => _tanks.Contains(f.TargetTankId!.Value)).ExecuteDeleteAsync();
        await db.TankSelectedMembers.Where(m => _tanks.Contains(m.TankId)).ExecuteDeleteAsync();
        await db.MediaItems.Where(m => _mediaItems.Contains(m.Id)).ExecuteDeleteAsync();
        await db.Comments.Where(c => c.TankId != null && _tanks.Contains(c.TankId.Value)).ExecuteDeleteAsync();
        await RemoveTanksAsync(fixture, _tanks.ToArray());
        await db.Profiles.Where(p => p.LegacyId >= 1_500_000_000).ExecuteUpdateAsync(s => s.SetProperty(p => p.LegacyId, (int?)null));
    }

    private static DateTimeOffset Ago(int days, int hours = 0) => DateTimeOffset.UtcNow.AddDays(-days).AddHours(-hours);

    private static string TodayText => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

    private async Task<Member> AwayMemberAsync(int awayDays = 30, string[]? interests = null)
    {
        var member = await NewMemberAsync(fixture);
        await using var db = fixture.CreateDbContext();
        await db.Profiles.Where(p => p.Id == member.ProfileId).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.AwaySince, Ago(awayDays))
            .SetProperty(p => p.Interests, interests ?? []));
        return member;
    }

    private async Task<long> TankAsync(long owner, TankVisibility profile = TankVisibility.Public, TankVisibility stream = TankVisibility.Public,
        TankRegion? region = null, string? title = null)
    {
        var id = await NewTankAsync(fixture, owner, profile, stream, title);
        _tanks.Add(id);
        if (region is not null)
        {
            await using var db = fixture.CreateDbContext();
            await db.Tanks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Region, region));
        }

        return id;
    }

    private async Task FollowAsync(Member viewer, long tank)
    {
        await using var db = fixture.CreateDbContext();
        db.Follows.Add(new Follow
        {
            FollowerProfileId = viewer.ProfileId, TargetTankId = tank, Status = FollowStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow, DecidedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<long> StoryAsync(long author, long? tank, DateTimeOffset published, string title = "A story", StoryState state = StoryState.Published)
    {
        await using var db = fixture.CreateDbContext();
        var story = new Story
        {
            AuthorProfileId = author, Title = title, State = state, CreatedAt = published, UpdatedAt = published, PublishedAt = published,
            Subjects = tank is null ? [] : [StorySubject.ForTank(tank.Value)],
        };
        db.Stories.Add(story);
        await db.SaveChangesAsync();
        _stories.Add(story.Id);
        return story.Id;
    }

    private async Task CareAsync(long tank, long author, DateTimeOffset at, CareKind kind = CareKind.WaterChange, bool onlyMe = false)
    {
        await using var db = fixture.CreateDbContext();
        db.CareEntries.Add(new CareEntry { TankId = tank, AuthorProfileId = author, Kind = kind, OccurredAt = at, RecordedAt = at, OnlyMe = onlyMe });
        await db.SaveChangesAsync();
    }

    private async Task TimeLapsePhotoAsync(long tank, long owner, DateTimeOffset at)
    {
        await using var db = fixture.CreateDbContext();
        var item = new MediaItem
        {
            StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, OwnerProfileId = owner, CreatedAt = at, ReceivedAt = at, CapturedInApp = true,
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        _mediaItems.Add(item.Id);
        var link = MediaLink.ForTank(tank, item.Id, MediaLinkRole.TimeLapse);
        link.CreatedAt = at;
        db.MediaLinks.Add(link);
        await db.SaveChangesAsync();
    }

    private async Task<long> CommentAsync(long? author, long? story, long? tank, DateTimeOffset at, bool deleted = false)
    {
        await using var db = fixture.CreateDbContext();
        var comment = new Comment
        {
            AuthorProfileId = author, StoryId = story, TankId = tank, Body = "A comment", CreatedAt = at, DeletedAt = deleted ? at : null,
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();
        _comments.Add(comment.Id);
        return comment.Id;
    }

    private async Task VoteAsync(long comment, long voter, DateTimeOffset at, short value = 1)
    {
        await using var db = fixture.CreateDbContext();
        db.CommentVotes.Add(new CommentVote { CommentId = comment, ProfileId = voter, Value = value, CreatedAt = at });
        await db.SaveChangesAsync();
    }

    private async Task SetProfileAsync(long id, Action<Profile> change)
    {
        await using var db = fixture.CreateDbContext();
        var profile = await db.Profiles.SingleAsync(p => p.Id == id);
        change(profile);
        await db.SaveChangesAsync();
    }

    private async Task<Profile> ProfileAsync(long id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    private async Task<(HttpStatusCode Status, JsonElement? Body)> GetAsync(Member viewer, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Route}?date={TodayText}&utcOffsetMinutes=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", viewer.Token);
        using var response = await (client ?? fixture.Client).SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK) return (response.StatusCode, null);
        return (response.StatusCode, await BodyAsync(response));
    }

    private async Task<JsonElement> RetrospectiveAsync(Member viewer)
    {
        var (status, body) = await GetAsync(viewer);
        Assert.Equal(HttpStatusCode.OK, status);
        return body!.Value;
    }

    private async Task AssertNoRetrospectiveAsync(Member viewer)
    {
        var (status, _) = await GetAsync(viewer);
        Assert.Equal(HttpStatusCode.NoContent, status);
    }

    private async Task<HttpStatusCode> SeenAsync(Member viewer, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Route}/seen");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", viewer.Token);
        using var response = await (client ?? fixture.Client).SendAsync(request);
        return response.StatusCode;
    }

    private static long[] TankIds(JsonElement body) =>
        body.GetProperty("followedTanks").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("tankId").GetInt64()).ToArray();

    private async Task<(Member Owner, long Tank)> OtherTankAsync(TankVisibility profile = TankVisibility.Public,
        TankVisibility stream = TankVisibility.Public, TankRegion? region = null, string? title = null)
    {
        var owner = await NewMemberAsync(fixture);
        return (owner, await TankAsync(owner.ProfileId, profile, stream, region, title));
    }

    [Fact]
    public async Task GivenAMemberAwayFor30DaysWithOverdueCareAndNewsOfAFollowedTank_WhenTheRetrospectiveLoads_ThenItShowsTheCareTheNewsAndTheNextSteps()
    {
        var member = await AwayMemberAsync();
        var own = await TankAsync(member.ProfileId, title: "My tank");
        await CareAsync(own, member.ProfileId, Ago(40));
        await TimeLapsePhotoAsync(own, member.ProfileId, Ago(60));
        var (owner, followed) = await OtherTankAsync(title: "Followed tank");
        await FollowAsync(member, followed);
        var story = await StoryAsync(owner.ProfileId, followed, Ago(5), "Spawning");

        var body = await RetrospectiveAsync(member);

        Assert.False(body.GetProperty("seen").GetBoolean());
        var overdue = body.GetProperty("overdueCare").GetProperty("items").EnumerateArray().Single();
        Assert.Equal(own, overdue.GetProperty("tankId").GetInt64());
        Assert.Equal("My tank", overdue.GetProperty("tankTitle").GetString());
        Assert.Equal("water_change", overdue.GetProperty("kind").GetString());
        Assert.True(overdue.GetProperty("daysOverdue").GetInt32() >= 30);
        var update = body.GetProperty("followedTanks").GetProperty("items").EnumerateArray().Single();
        Assert.Equal(followed, update.GetProperty("tankId").GetInt64());
        Assert.Equal(story, update.GetProperty("update").GetProperty("story").GetProperty("id").GetInt64());
        var steps = body.GetProperty("nextSteps");
        Assert.Equal(new[] { own }, steps.GetProperty("careTanks").EnumerateArray().Select(t => t.GetProperty("tankId").GetInt64()));
        Assert.Equal(new[] { own }, steps.GetProperty("timeLapseTanks").EnumerateArray().Select(t => t.GetProperty("tankId").GetInt64()));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("responses").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("regionNews").ValueKind);
    }

    [Theory]
    [InlineData(27, false)]
    [InlineData(28, true)]
    public async Task GivenAGapBeforeTheRequest_WhenTheRetrospectiveLoads_ThenItIsDueFrom28Days(int gapDays, bool due)
    {
        var member = await NewMemberAsync(fixture);
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));
        await SetProfileAsync(member.ProfileId, p => p.LastSeenAt = Ago(gapDays, 1));
        using (var touch = await SendAsync(fixture, HttpMethod.Get, "/api/me", member.Token))
            Assert.Equal(HttpStatusCode.OK, touch.StatusCode);

        var (status, _) = await GetAsync(member);

        Assert.Equal(due ? HttpStatusCode.OK : HttpStatusCode.NoContent, status);
    }

    [Fact]
    public async Task GivenAMemberWhoNeverWasAway_WhenTheRetrospectiveLoads_ThenThereIsNone()
    {
        var member = await NewMemberAsync(fixture);
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));

        await AssertNoRetrospectiveAsync(member);
    }

    [Fact]
    public async Task GivenAMemberWhoSawAReturnRetrospective40DaysAgo_WhenTheMemberReturnsAfterAnotherAbsence_ThenNoRetrospectiveOpens()
    {
        var member = await AwayMemberAsync(awayDays: 29);
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));
        await SetProfileAsync(member.ProfileId, p =>
        {
            p.ReturnRetrospectiveSeenAt = Ago(40);
            p.ReturnRetrospectiveSince = Ago(70);
        });

        await AssertNoRetrospectiveAsync(member);
    }

    [Fact]
    public async Task GivenAReturnRetrospectiveSeenMoreThan91DaysAgo_WhenTheMemberReturns_ThenANewOneIsDue()
    {
        var member = await AwayMemberAsync();
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));
        await SetProfileAsync(member.ProfileId, p =>
        {
            p.ReturnRetrospectiveSeenAt = Ago(92);
            p.ReturnRetrospectiveSince = Ago(130);
        });

        var body = await RetrospectiveAsync(member);

        Assert.False(body.GetProperty("seen").GetBoolean());
    }

    [Fact]
    public async Task GivenNoCardWithData_WhenTheRetrospectiveLoads_ThenThereIsNone()
    {
        var member = await AwayMemberAsync();
        await TankAsync(member.ProfileId);

        await AssertNoRetrospectiveAsync(member);
    }

    [Fact]
    public async Task GivenAnArchiveRetrospectiveThatIsDue_WhenTheRetrospectiveLoads_ThenItWaitsAndSeenChangesNothing()
    {
        var member = await AwayMemberAsync();
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));
        using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddScoped<IArchiveRetrospectiveGate>(_ => new FixedArchiveGate(true))));
        using var client = factory.CreateClient();

        var (status, _) = await GetAsync(member, client);
        var seen = await SeenAsync(member, client);

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(HttpStatusCode.NoContent, seen);
        var profile = await ProfileAsync(member.ProfileId);
        Assert.NotNull(profile.AwaySince);
        Assert.Null(profile.ReturnRetrospectiveSeenAt);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(member)).Status);
    }

    [Fact]
    public async Task GivenArchiveContentThatTheMemberHasNotSeen_WhenTheRetrospectiveLoads_ThenTheArchiveRetrospectiveWinsUntilItIsSeen()
    {
        var member = await AwayMemberAsync();
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));
        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == own).ExecuteUpdateAsync(s => s.SetProperty(t => t.LegacyId, Random.Shared.Next(1_500_000_000, int.MaxValue)));
            await db.Profiles.Where(p => p.Id == member.ProfileId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LegacyId, Random.Shared.Next(1_500_000_000, int.MaxValue)));
        }

        await AssertNoRetrospectiveAsync(member);
        Assert.Equal(HttpStatusCode.NoContent, await SeenAsync(member));
        Assert.NotNull((await ProfileAsync(member.ProfileId)).AwaySince);

        await SetProfileAsync(member.ProfileId, p => p.ArchiveRetrospectiveSeenAt = DateTimeOffset.UtcNow);

        Assert.False((await RetrospectiveAsync(member)).GetProperty("seen").GetBoolean());
    }

    [Fact]
    public async Task GivenAMemberWithoutLegacyData_WhenTheRetrospectiveLoads_ThenTheArchiveRetrospectiveDoesNotWait()
    {
        var member = await AwayMemberAsync();
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));

        Assert.False((await RetrospectiveAsync(member)).GetProperty("seen").GetBoolean());
    }

    private sealed class FixedArchiveGate(bool due) : IArchiveRetrospectiveGate
    {
        public Task<bool> IsDueAsync(long profileId, CancellationToken cancellationToken) => Task.FromResult(due);
    }

    [Fact]
    public async Task GivenCareOfOtherMembersAndFutureDueDates_WhenTheRetrospectiveLoads_ThenOnlyOverdueCareOfOwnTanksShows()
    {
        var member = await AwayMemberAsync();
        var overdue = await TankAsync(member.ProfileId);
        await CareAsync(overdue, member.ProfileId, Ago(20));
        var fresh = await TankAsync(member.ProfileId);
        await CareAsync(fresh, member.ProfileId, Ago(1));
        var never = await TankAsync(member.ProfileId);
        var (otherOwner, other) = await OtherTankAsync();
        await CareAsync(other, otherOwner.ProfileId, Ago(60));

        var body = await RetrospectiveAsync(member);

        var items = body.GetProperty("overdueCare").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { overdue }, items.Select(i => i.GetProperty("tankId").GetInt64()));
        Assert.DoesNotContain(fresh, items.Select(i => i.GetProperty("tankId").GetInt64()));
        Assert.DoesNotContain(never, items.Select(i => i.GetProperty("tankId").GetInt64()));
    }

    [Fact]
    public async Task GivenKudosAndCommentsSinceTheAbsenceBegan_WhenTheRetrospectiveLoads_ThenTheyAreCounted()
    {
        var member = await AwayMemberAsync();
        var voter = await NewMemberAsync(fixture);
        var second = await NewMemberAsync(fixture);
        var own = await TankAsync(member.ProfileId);
        var story = await StoryAsync(member.ProfileId, own, Ago(100));
        var ownComment = await CommentAsync(member.ProfileId, story, null, Ago(100));
        await VoteAsync(ownComment, voter.ProfileId, Ago(3));
        await VoteAsync(ownComment, second.ProfileId, Ago(2));
        await VoteAsync(ownComment, await NewVoterAsync(), Ago(1), value: -1);
        await VoteAsync(ownComment, await NewVoterAsync(), Ago(90));
        await CommentAsync(voter.ProfileId, story, null, Ago(4));
        await CommentAsync(second.ProfileId, null, own, Ago(2));
        await CommentAsync(second.ProfileId, null, own, Ago(2), deleted: true);
        await CommentAsync(second.ProfileId, story, null, Ago(90));
        await CommentAsync(member.ProfileId, story, null, Ago(2));

        var responses = (await RetrospectiveAsync(member)).GetProperty("responses");

        Assert.Equal(2, responses.GetProperty("kudos").GetInt32());
        Assert.Equal(2, responses.GetProperty("comments").GetInt32());

        async Task<long> NewVoterAsync() => (await NewMemberAsync(fixture)).ProfileId;
    }

    [Fact]
    public async Task GivenKudosOnCommentsWhoseContentIsGoneOrHidden_WhenTheRetrospectiveLoads_ThenOnlyKudosOnVisibleContentCount()
    {
        var member = await AwayMemberAsync();
        var voter = await NewMemberAsync(fixture);
        var (owner, publicTank) = await OtherTankAsync();
        var (_, privateTank) = await OtherTankAsync(TankVisibility.Private, TankVisibility.Private);
        var (_, deletedTank) = await OtherTankAsync();
        var publishedStory = await StoryAsync(owner.ProfileId, null, Ago(100));
        var draftStory = await StoryAsync(owner.ProfileId, null, Ago(100), state: StoryState.Draft);
        var deletedStory = await StoryAsync(owner.ProfileId, null, Ago(100));
        await using (var db = fixture.CreateDbContext())
        {
            await db.Stories.Where(s => s.Id == deletedStory).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, DateTimeOffset.UtcNow));
            await db.Tanks.Where(t => t.Id == deletedTank).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, DateTimeOffset.UtcNow));
        }

        foreach (var (story, tank) in new (long?, long?)[] { (publishedStory, null), (draftStory, null), (deletedStory, null), (null, publicTank), (null, privateTank), (null, deletedTank) })
        {
            var comment = await CommentAsync(member.ProfileId, story, tank, Ago(100));
            await VoteAsync(comment, voter.ProfileId, Ago(2));
        }

        var responses = (await RetrospectiveAsync(member)).GetProperty("responses");

        Assert.Equal(2, responses.GetProperty("kudos").GetInt32());
        Assert.Equal(0, responses.GetProperty("comments").GetInt32());
    }

    [Fact]
    public async Task GivenCommentsOfGuestsAndSuspendedAuthors_WhenTheRetrospectiveLoads_ThenOnlyGuestsAndActiveMembersCount()
    {
        var member = await AwayMemberAsync();
        var own = await TankAsync(member.ProfileId);
        var active = await NewMemberAsync(fixture);
        var suspended = await NewMemberAsync(fixture);
        await SetProfileAsync(suspended.ProfileId, p => p.SuspendedUntil = DateTimeOffset.UtcNow.AddDays(3));
        foreach (var author in new long?[] { active.ProfileId, suspended.ProfileId, null })
            await CommentAsync(author, null, own, Ago(2));

        var responses = (await RetrospectiveAsync(member)).GetProperty("responses");

        Assert.Equal(2, responses.GetProperty("comments").GetInt32());
    }

    [Fact]
    public async Task GivenAnAbsenceThatCannotBeShown_WhenTheRetrospectiveLoads_ThenTheAbsenceLapses()
    {
        var blocked = await AwayMemberAsync();
        var ownBlocked = await TankAsync(blocked.ProfileId);
        await CareAsync(ownBlocked, blocked.ProfileId, Ago(60));
        await SetProfileAsync(blocked.ProfileId, p => p.ReturnRetrospectiveSeenAt = Ago(40));
        var empty = await AwayMemberAsync();

        await AssertNoRetrospectiveAsync(blocked);
        await AssertNoRetrospectiveAsync(empty);

        Assert.Null((await ProfileAsync(blocked.ProfileId)).AwaySince);
        Assert.Null((await ProfileAsync(empty.ProfileId)).AwaySince);
        Assert.NotNull((await ProfileAsync(blocked.ProfileId)).ReturnRetrospectiveSeenAt);
    }

    [Fact]
    public async Task GivenALapsedAbsenceAndLaterANewOneAfterTheQuarter_WhenTheMemberReturns_ThenTheRetrospectiveOpens()
    {
        var member = await AwayMemberAsync();
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));
        await SetProfileAsync(member.ProfileId, p => p.ReturnRetrospectiveSeenAt = Ago(40));
        await AssertNoRetrospectiveAsync(member);

        await SetProfileAsync(member.ProfileId, p =>
        {
            p.ReturnRetrospectiveSeenAt = Ago(95);
            p.AwaySince = Ago(30);
        });

        Assert.False((await RetrospectiveAsync(member)).GetProperty("seen").GetBoolean());
    }

    [Fact]
    public async Task GivenADueArchiveRetrospective_WhenTheRetrospectiveLoads_ThenTheAbsenceStaysRecorded()
    {
        var member = await AwayMemberAsync();
        using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddScoped<IArchiveRetrospectiveGate>(_ => new FixedArchiveGate(true))));
        using var client = factory.CreateClient();

        await GetAsync(member, client);

        Assert.NotNull((await ProfileAsync(member.ProfileId)).AwaySince);
    }

    [Fact]
    public async Task GivenTheStateOfTheProfile_WhenMeLoads_ThenTheReturnRetrospectiveStatusFollowsTheColumnsAlone()
    {
        var due = await AwayMemberAsync();
        var seen = await AwayMemberAsync();
        await SetProfileAsync(seen.ProfileId, p =>
        {
            p.AwaySince = null;
            p.ReturnRetrospectiveSeenAt = Ago(5);
            p.ReturnRetrospectiveSince = Ago(40);
        });
        var old = await AwayMemberAsync();
        await SetProfileAsync(old.ProfileId, p =>
        {
            p.AwaySince = null;
            p.ReturnRetrospectiveSeenAt = Ago(31);
            p.ReturnRetrospectiveSince = Ago(60);
        });
        var blocked = await AwayMemberAsync();
        await SetProfileAsync(blocked.ProfileId, p => p.ReturnRetrospectiveSeenAt = Ago(40));
        var none = await NewMemberAsync(fixture);

        async Task<JsonElement> StatusAsync(Member m)
        {
            using var response = await SendAsync(fixture, HttpMethod.Get, "/api/me", m.Token);
            return (await BodyAsync(response)).GetProperty("returnRetrospective");
        }

        Assert.True((await StatusAsync(due)).GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await StatusAsync(due)).GetProperty("seenAt").ValueKind);
        Assert.True((await StatusAsync(seen)).GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.String, (await StatusAsync(seen)).GetProperty("seenAt").ValueKind);
        Assert.False((await StatusAsync(old)).GetProperty("available").GetBoolean());
        Assert.False((await StatusAsync(blocked)).GetProperty("available").GetBoolean());
        Assert.False((await StatusAsync(none)).GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task GivenCommentsOnADraftStoryOrADeletedTank_WhenTheRetrospectiveLoads_ThenTheyAreNotCounted()
    {
        var member = await AwayMemberAsync();
        var other = await NewMemberAsync(fixture);
        var draft = await StoryAsync(member.ProfileId, null, Ago(5), state: StoryState.Draft);
        await CommentAsync(other.ProfileId, draft, null, Ago(2));
        await TankAsync(member.ProfileId);

        await AssertNoRetrospectiveAsync(member);
    }

    [Fact]
    public async Task GivenNewTanksAndStoriesInTheRegionsOfTheMember_WhenTheRetrospectiveLoads_ThenThePublicOnesShow()
    {
        var member = await AwayMemberAsync(interests: ["lake_malawi", "south_america"]);
        var (_, malawi) = await OtherTankAsync(region: TankRegion.LakeMalawi, title: "Malawi tank");
        var (_, unassigned) = await OtherTankAsync(region: TankRegion.AmericasUnassigned, title: "Americas tank");
        var (_, tanganyika) = await OtherTankAsync(region: TankRegion.LakeTanganyika);
        var (_, hidden) = await OtherTankAsync(profile: TankVisibility.Followers, stream: TankVisibility.Followers, region: TankRegion.LakeMalawi);
        var (storyOwner, storyTank) = await OtherTankAsync(region: TankRegion.LakeMalawi);
        var story = await StoryAsync(storyOwner.ProfileId, storyTank, Ago(2), "Regional story");
        await StoryAsync(storyOwner.ProfileId, tanganyika, Ago(2), "Other region");
        await StoryAsync(storyOwner.ProfileId, hidden, Ago(2), "Hidden tank story");
        var ownTank = await TankAsync(member.ProfileId, region: TankRegion.LakeMalawi);

        var news = (await RetrospectiveAsync(member)).GetProperty("regionNews");

        var tanks = news.GetProperty("tanks").EnumerateArray().Select(t => t.GetProperty("tankId").GetInt64()).ToArray();
        Assert.Contains(malawi, tanks);
        Assert.Contains(unassigned, tanks);
        Assert.Contains(storyTank, tanks);
        Assert.DoesNotContain(tanganyika, tanks);
        Assert.DoesNotContain(hidden, tanks);
        Assert.DoesNotContain(ownTank, tanks);
        Assert.Equal(new[] { story }, news.GetProperty("stories").EnumerateArray().Select(s => s.GetProperty("id").GetInt64()));
    }

    [Fact]
    public async Task GivenNoInterests_WhenTheRetrospectiveLoads_ThenThereIsNoRegionNews()
    {
        var member = await AwayMemberAsync();
        await OtherTankAsync(region: TankRegion.LakeMalawi);

        await AssertNoRetrospectiveAsync(member);
    }

    [Fact]
    public async Task GivenTanksBeforeTheAbsenceOrOfSuspendedOwners_WhenTheRetrospectiveLoads_ThenRegionNewsLeavesThemOut()
    {
        var member = await AwayMemberAsync(interests: ["lake_malawi"]);
        var (_, old) = await OtherTankAsync(region: TankRegion.LakeMalawi);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == old).ExecuteUpdateAsync(s => s.SetProperty(t => t.PublishedAt, Ago(50)));
        }

        var (suspendedOwner, _) = await OtherTankAsync(region: TankRegion.LakeMalawi);
        await SetProfileAsync(suspendedOwner.ProfileId, p => p.SuspendedUntil = DateTimeOffset.UtcNow.AddDays(3));

        await AssertNoRetrospectiveAsync(member);
    }

    [Fact]
    public async Task GivenNewsOfTanksTheMemberMayNotSee_WhenTheRetrospectiveLoads_ThenNeitherTheNewsNorACountOfItAppears()
    {
        var member = await AwayMemberAsync(interests: ["lake_malawi"]);
        var (privateOwner, privateTank) = await OtherTankAsync(TankVisibility.Private, TankVisibility.Private, TankRegion.LakeMalawi, "Secret private");
        var (followersOwner, followersTank) = await OtherTankAsync(TankVisibility.Followers, TankVisibility.Followers, TankRegion.LakeMalawi, "Secret followers");
        var (selectedOwner, selectedTank) = await OtherTankAsync(TankVisibility.Selected, TankVisibility.Selected, TankRegion.LakeMalawi, "Secret selected");
        var (streamOwner, streamTank) = await OtherTankAsync(TankVisibility.Public, TankVisibility.Private, title: "Stream private");
        var (onlyMeOwner, onlyMeTank) = await OtherTankAsync(title: "Only me care");
        var (suspendedOwner, suspendedTank) = await OtherTankAsync(title: "Suspended owner");
        var (provisionalOwner, provisionalTank) = await OtherTankAsync(title: "Provisional owner");
        foreach (var tank in new[] { privateTank, selectedTank, streamTank, onlyMeTank, suspendedTank, provisionalTank })
            await FollowAsync(member, tank);
        foreach (var (owner, tank) in new[]
                 {
                     (privateOwner, privateTank), (followersOwner, followersTank), (selectedOwner, selectedTank), (suspendedOwner, suspendedTank),
                     (provisionalOwner, provisionalTank),
                 })
        {
            await StoryAsync(owner.ProfileId, tank, Ago(2), "Hidden news");
            await CareAsync(tank, owner.ProfileId, Ago(2), CareKind.Measurement);
        }

        await CareAsync(streamTank, streamOwner.ProfileId, Ago(2), CareKind.Measurement);
        await CareAsync(onlyMeTank, onlyMeOwner.ProfileId, Ago(2), CareKind.Measurement, onlyMe: true);
        await SetProfileAsync(suspendedOwner.ProfileId, p => p.SuspendedUntil = DateTimeOffset.UtcNow.AddDays(3));
        await SetProfileAsync(provisionalOwner.ProfileId, p => p.Kind = ProfileKind.Provisional);

        await AssertNoRetrospectiveAsync(member);
    }

    [Fact]
    public async Task GivenVisibleAndHiddenNews_WhenTheRetrospectiveLoads_ThenOnlyTheVisibleNewsAppearsWithoutAnyCountOfTheHiddenNews()
    {
        var member = await AwayMemberAsync();
        var (visibleOwner, visible) = await OtherTankAsync(title: "Visible followed");
        var (hiddenOwner, hidden) = await OtherTankAsync(TankVisibility.Private, TankVisibility.Private, title: "Hidden followed");
        var (followersOwner, followersOnly) = await OtherTankAsync(TankVisibility.Followers, TankVisibility.Followers, title: "Followers followed");
        var (selectedOwner, selected) = await OtherTankAsync(TankVisibility.Selected, TankVisibility.Selected, title: "Selected followed");
        foreach (var tank in new[] { visible, hidden, followersOnly, selected }) await FollowAsync(member, tank);
        await using (var db = fixture.CreateDbContext())
        {
            db.TankSelectedMembers.Add(new TankSelectedMember { TankId = selected, MemberProfileId = member.ProfileId, AddedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        foreach (var (owner, tank) in new[] { (visibleOwner, visible), (hiddenOwner, hidden), (followersOwner, followersOnly), (selectedOwner, selected) })
            await StoryAsync(owner.ProfileId, tank, Ago(2));

        var raw = (await RetrospectiveAsync(member)).GetRawText();
        var body = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(new[] { visible, followersOnly, selected }.OrderBy(i => i), TankIds(body).OrderBy(i => i));
        Assert.DoesNotContain("Hidden followed", raw);
        Assert.False(body.GetProperty("followedTanks").GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task GivenACareStreamThatIsPrivate_WhenTheRetrospectiveLoads_ThenAStoryOfTheTankStillShowsButNoCareEntry()
    {
        var member = await AwayMemberAsync();
        var (owner, tank) = await OtherTankAsync(TankVisibility.Public, TankVisibility.Private);
        await FollowAsync(member, tank);
        await CareAsync(tank, owner.ProfileId, Ago(1), CareKind.Measurement);
        var story = await StoryAsync(owner.ProfileId, tank, Ago(3));

        var update = (await RetrospectiveAsync(member)).GetProperty("followedTanks").GetProperty("items").EnumerateArray().Single().GetProperty("update");

        Assert.Equal("story", update.GetProperty("kind").GetString());
        Assert.Equal(story, update.GetProperty("story").GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task GivenUpdatesBeforeTheAbsenceBegan_WhenTheRetrospectiveLoads_ThenTheyAreLeftOut()
    {
        var member = await AwayMemberAsync(awayDays: 10);
        var (owner, tank) = await OtherTankAsync();
        await FollowAsync(member, tank);
        await StoryAsync(owner.ProfileId, tank, Ago(12));

        await AssertNoRetrospectiveAsync(member);
    }

    [Fact]
    public async Task GivenAPendingRetrospective_WhenTheMemberMarksItSeen_ThenTheTimeIsStoredAndTheAbsenceCleared()
    {
        var member = await AwayMemberAsync();
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));
        var before = DateTimeOffset.UtcNow;
        var away = (await ProfileAsync(member.ProfileId)).AwaySince;

        Assert.Equal(HttpStatusCode.NoContent, await SeenAsync(member));

        var profile = await ProfileAsync(member.ProfileId);
        Assert.Null(profile.AwaySince);
        Assert.InRange(profile.ReturnRetrospectiveSeenAt!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(away, profile.ReturnRetrospectiveSince);
    }

    [Fact]
    public async Task GivenASeenRetrospective_WhenTheMemberLoadsItWithin30Days_ThenTheSameCardsShowAsSeenAndAfterwardsNothing()
    {
        var member = await AwayMemberAsync();
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));
        await SeenAsync(member);

        var body = await RetrospectiveAsync(member);

        Assert.True(body.GetProperty("seen").GetBoolean());
        Assert.Equal(own, body.GetProperty("overdueCare").GetProperty("items").EnumerateArray().Single().GetProperty("tankId").GetInt64());

        await SetProfileAsync(member.ProfileId, p => p.ReturnRetrospectiveSeenAt = Ago(31));
        await AssertNoRetrospectiveAsync(member);
    }

    [Fact]
    public async Task GivenASeenRetrospective_WhenTheMemberMarksItSeenAgain_ThenNothingChanges()
    {
        var member = await AwayMemberAsync();
        var own = await TankAsync(member.ProfileId);
        await CareAsync(own, member.ProfileId, Ago(60));
        await SeenAsync(member);
        var first = await ProfileAsync(member.ProfileId);

        Assert.Equal(HttpStatusCode.NoContent, await SeenAsync(member));

        var second = await ProfileAsync(member.ProfileId);
        Assert.Equal(first.ReturnRetrospectiveSeenAt, second.ReturnRetrospectiveSeenAt);
        Assert.Equal(first.ReturnRetrospectiveSince, second.ReturnRetrospectiveSince);
    }

    [Fact]
    public async Task GivenNoAbsence_WhenTheMemberMarksTheRetrospectiveSeen_ThenNothingIsStored()
    {
        var member = await NewMemberAsync(fixture);

        Assert.Equal(HttpStatusCode.NoContent, await SeenAsync(member));

        Assert.Null((await ProfileAsync(member.ProfileId)).ReturnRetrospectiveSeenAt);
    }

    [Fact]
    public async Task GivenAnAbsenceWithinTheQuarter_WhenTheMemberMarksTheRetrospectiveSeen_ThenTheQuarterIsNotExtended()
    {
        var member = await AwayMemberAsync();
        var seenAt = Ago(40);
        await SetProfileAsync(member.ProfileId, p => p.ReturnRetrospectiveSeenAt = seenAt);

        Assert.Equal(HttpStatusCode.NoContent, await SeenAsync(member));

        var profile = await ProfileAsync(member.ProfileId);
        Assert.NotNull(profile.AwaySince);
        Assert.Equal(seenAt.ToUnixTimeMilliseconds(), profile.ReturnRetrospectiveSeenAt!.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task GivenProvisionalAndAnonymousCallers_WhenTheyUseTheRoutes_ThenTheyAreRefused()
    {
        using var provisional = await NewAsync(fixture.Client);

        using var get = await SendAsync(fixture, HttpMethod.Get, $"{Route}?date={TodayText}", provisional.Token);
        using var post = await SendAsync(fixture, HttpMethod.Post, $"{Route}/seen", provisional.Token);
        using var anonymous = await SendAsync(fixture, HttpMethod.Get, $"{Route}?date={TodayText}");

        await AssertRegistrationRequiredAsync(get);
        await AssertRegistrationRequiredAsync(post);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task GivenAnInvalidDate_WhenTheRetrospectiveLoads_ThenTheRequestIsRefused()
    {
        var member = await AwayMemberAsync();

        using var response = await SendAsync(fixture, HttpMethod.Get, $"{Route}?date=yesterday", member.Token);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "care_date_invalid");
    }
}
