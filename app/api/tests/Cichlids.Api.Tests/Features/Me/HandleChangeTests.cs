// Story: TASK-3.43
using System.Net;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Me;

/// <summary>
/// The public handle of a member: the rules for a new handle, the reservation of released handles,
/// the waiting time between two changes and the guarantees under concurrent requests.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-3.43")]
public sealed class HandleChangeTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Route = "/api/me/handle";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.OutboxEvents.Where(e => e.AggregateType == "profile").ExecuteDeleteAsync();
    }

    private static string NewHandle() => $"h{Guid.NewGuid():N}"[..14];

    /// <summary>
    /// A member whose handle has the form a member can choose, so other members can ask for it.
    /// </summary>
    private async Task<Member> NewChosenMemberAsync()
    {
        var member = await NewMemberAsync(fixture);
        await using var db = fixture.CreateDbContext();
        await db.Profiles.Where(p => p.Id == member.ProfileId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Username, NewHandle()));
        return member;
    }

    private Task<HttpResponseMessage> ChangeAsync(Member member, string? handle) =>
        SendAsync(fixture, HttpMethod.Put, Route, member.Token, new { handle });

    private async Task<Profile> ProfileAsync(long id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    private async Task BackdateAsync(long profileId, int days)
    {
        await using var db = fixture.CreateDbContext();
        await db.Profiles.Where(p => p.Id == profileId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.HandleChangedAt, DateTimeOffset.UtcNow.AddDays(-days)));
    }

    [Fact]
    public async Task GivenARegisteredMember_WhenSettingAValidFreeHandle_ThenTheProfileShowsItAndTheOldOneIsReservedForNinetyDays()
    {
        var member = await NewMemberAsync(fixture);
        var old = (await ProfileAsync(member.ProfileId)).Username;
        var handle = NewHandle();
        var before = DateTimeOffset.UtcNow;

        using var response = await ChangeAsync(member, handle);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(handle, body.GetProperty("handle").GetString());
        Assert.InRange(body.GetProperty("nextChangeAt").GetDateTimeOffset(), before.AddDays(30), DateTimeOffset.UtcNow.AddDays(30));

        using var me = await SendAsync(fixture, HttpMethod.Get, "/api/me", member.Token);
        Assert.Equal(handle, (await BodyAsync(me)).GetProperty("profile").GetProperty("username").GetString());
        using var profile = await SendAsync(fixture, HttpMethod.Get, $"/api/profiles/{member.ProfileId}");
        Assert.Equal(handle, (await BodyAsync(profile)).GetProperty("username").GetString());

        await using var db = fixture.CreateDbContext();
        var released = await db.ReleasedHandles.SingleAsync(r => r.ProfileId == member.ProfileId);
        Assert.Equal(old, released.Handle);
        Assert.InRange(released.ReservedUntil - released.ReleasedAt, TimeSpan.FromDays(90), TimeSpan.FromDays(90));
        Assert.InRange(released.ReleasedAt, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GivenAHandleWithCapitals_WhenSettingIt_ThenItIsStoredLowercase()
    {
        var member = await NewMemberAsync(fixture);
        var handle = NewHandle();

        using var response = await ChangeAsync(member, $"  {handle.ToUpperInvariant()} ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(handle, (await ProfileAsync(member.ProfileId)).Username);
    }

    [Fact]
    public async Task GivenAChange_WhenItSucceeds_ThenTheEventNamesOnlyIds()
    {
        var member = await NewMemberAsync(fixture);

        using var response = await ChangeAsync(member, NewHandle());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        var events = await db.OutboxEvents.Where(e => e.EventType == "profile.handle_changed" && e.AggregateId == member.ProfileId.ToString()).ToListAsync();
        var outbox = Assert.Single(events);
        Assert.Equal("profile", outbox.AggregateType);
        var payload = JsonDocument.Parse(outbox.Payload).RootElement;
        Assert.Equal(["profileId"], payload.EnumerateObject().Select(p => p.Name));
        Assert.Equal(member.ProfileId, payload.GetProperty("profileId").GetInt64());
    }

    [Fact]
    public async Task GivenAMemberWhoChangedTheHandleRecently_WhenChangingAgain_ThenTheApiNamesTheDateOfTheNextChange()
    {
        var member = await NewMemberAsync(fixture);
        using var first = await ChangeAsync(member, NewHandle());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await BackdateAsync(member.ProfileId, 29);
        var changedAt = (await ProfileAsync(member.ProfileId)).HandleChangedAt!.Value;

        using var second = await ChangeAsync(member, NewHandle());

        await AssertProblemAsync(second, HttpStatusCode.TooManyRequests, "handle_change_too_soon");
        var problem = await BodyAsync(second);
        Assert.Equal(changedAt.AddDays(30), problem.GetProperty("nextChangeAt").GetDateTimeOffset());
        Assert.NotNull(second.Headers.RetryAfter);
        using var me = await SendAsync(fixture, HttpMethod.Get, "/api/me", member.Token);
        Assert.Equal(changedAt.AddDays(30), (await BodyAsync(me)).GetProperty("handleChangeAvailableAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task GivenAChangeOlderThanThirtyDays_WhenChangingAgain_ThenTheHandleChanges()
    {
        var member = await NewMemberAsync(fixture);
        using var first = await ChangeAsync(member, NewHandle());
        await BackdateAsync(member.ProfileId, 31);
        var handle = NewHandle();

        using var second = await ChangeAsync(member, handle);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(handle, (await ProfileAsync(member.ProfileId)).Username);
        using var me = await SendAsync(fixture, HttpMethod.Get, "/api/me", member.Token);
        Assert.True((await BodyAsync(me)).GetProperty("handleChangeAvailableAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GivenAMemberWhoNeverChangedTheHandle_WhenReadingMe_ThenAChangeIsAvailable()
    {
        var member = await NewMemberAsync(fixture);

        using var me = await SendAsync(fixture, HttpMethod.Get, "/api/me", member.Token);

        Assert.Equal(JsonValueKind.Null, (await BodyAsync(me)).GetProperty("handleChangeAvailableAt").ValueKind);
    }

    [Fact]
    public async Task GivenTheCurrentHandle_WhenSettingItAgain_ThenNothingChanges()
    {
        var member = await NewChosenMemberAsync();
        var current = (await ProfileAsync(member.ProfileId)).Username;

        using var response = await ChangeAsync(member, current);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await ProfileAsync(member.ProfileId);
        Assert.Null(profile.HandleChangedAt);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.ReleasedHandles.AnyAsync(r => r.ProfileId == member.ProfileId));
    }

    [Fact]
    public async Task GivenAHandleOfAnotherMember_WhenAskingForItInAnyCase_ThenItIsTaken()
    {
        var owner = await NewChosenMemberAsync();
        var taker = await NewMemberAsync(fixture);
        var handle = (await ProfileAsync(owner.ProfileId)).Username;

        using var response = await ChangeAsync(taker, handle.ToUpperInvariant());

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "handle_taken");
        Assert.NotEqual(handle, (await ProfileAsync(taker.ProfileId)).Username);
    }

    [Theory]
    [InlineData("a", "handle_invalid")]
    [InlineData("", "handle_invalid")]
    [InlineData("1abc", "handle_invalid")]
    [InlineData("has space", "handle_invalid")]
    [InlineData("two..dots", "handle_invalid")]
    [InlineData("someone@example.test", "handle_invalid")]
    [InlineData("abcdefghijklmnopqrstuvwxyz01234", "handle_invalid")]
    [InlineData("user12345678", "handle_generated")]
    [InlineData("guest_17", "handle_generated")]
    [InlineData("provisional-0123", "handle_generated")]
    [InlineData("admin", "handle_reserved")]
    [InlineData("Official", "handle_reserved")]
    [InlineData("community-archive", "handle_reserved")]
    public async Task GivenAnUnusableHandle_WhenAskingForIt_ThenTheApiAnswersWithTheCodeOfTheReason(string handle, string code)
    {
        var member = await NewMemberAsync(fixture);
        var before = (await ProfileAsync(member.ProfileId)).Username;

        using var response = await ChangeAsync(member, handle);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        Assert.Equal(before, (await ProfileAsync(member.ProfileId)).Username);
    }

    [Fact]
    public async Task GivenNoHandleInTheBody_WhenAskingForOne_ThenTheHandleIsInvalid()
    {
        var member = await NewMemberAsync(fixture);

        using var response = await SendAsync(fixture, HttpMethod.Put, Route, member.Token, new { });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "handle_invalid");
    }

    [Fact]
    public async Task GivenAReleasedHandle_WhenAnotherMemberAsksForIt_ThenItIsTakenButTheFormerOwnerMayTakeItBack()
    {
        var former = await NewChosenMemberAsync();
        var other = await NewMemberAsync(fixture);
        var released = (await ProfileAsync(former.ProfileId)).Username;
        using var change = await ChangeAsync(former, NewHandle());
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        using var refused = await ChangeAsync(other, released);
        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "handle_taken");

        await BackdateAsync(former.ProfileId, 31);
        using var backAgain = await ChangeAsync(former, released);
        Assert.Equal(HttpStatusCode.OK, backAgain.StatusCode);
        Assert.Equal(released, (await ProfileAsync(former.ProfileId)).Username);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.ReleasedHandles.AnyAsync(r => r.Handle == released));
    }

    [Fact]
    public async Task GivenAReservationThatEnded_WhenAnotherMemberAsksForTheHandle_ThenHeTakesIt()
    {
        var former = await NewChosenMemberAsync();
        var other = await NewMemberAsync(fixture);
        var released = (await ProfileAsync(former.ProfileId)).Username;
        using var change = await ChangeAsync(former, NewHandle());
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        await using (var db = fixture.CreateDbContext())
        {
            await db.ReleasedHandles.Where(r => r.Handle == released)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.ReleasedAt, DateTimeOffset.UtcNow.AddDays(-100))
                    .SetProperty(r => r.ReservedUntil, DateTimeOffset.UtcNow.AddDays(-10)));
        }

        using var response = await ChangeAsync(other, released);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(released, (await ProfileAsync(other.ProfileId)).Username);
        await using var check = fixture.CreateDbContext();
        Assert.False(await check.ReleasedHandles.AnyAsync(r => r.Handle == released));
    }

    [Fact]
    public async Task GivenAHandleChangedBackAndForth_WhenReleasedTwice_ThenOneReservationHoldsTheHandle()
    {
        var member = await NewChosenMemberAsync();
        var first = (await ProfileAsync(member.ProfileId)).Username;
        var second = NewHandle();
        using var toSecond = await ChangeAsync(member, second);
        await BackdateAsync(member.ProfileId, 31);
        using var toFirst = await ChangeAsync(member, first);
        await BackdateAsync(member.ProfileId, 31);

        using var toSecondAgain = await ChangeAsync(member, second);

        Assert.Equal(HttpStatusCode.OK, toSecondAgain.StatusCode);
        await using var db = fixture.CreateDbContext();
        var reserved = await db.ReleasedHandles.Where(r => r.ProfileId == member.ProfileId).Select(r => r.Handle).ToListAsync();
        Assert.Equal([first], reserved);
    }

    [Fact]
    public async Task GivenAHandleWithCapitals_WhenTheMemberLowercasesIt_ThenTheProfileShowsTheLowercaseForm()
    {
        var member = await NewMemberAsync(fixture);
        var mixed = $"Mixed{Guid.NewGuid():N}"[..14];
        await using (var db = fixture.CreateDbContext())
        {
            await db.Profiles.Where(p => p.Id == member.ProfileId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Username, mixed));
        }

        using var response = await ChangeAsync(member, mixed.ToLowerInvariant());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mixed.ToLowerInvariant(), (await ProfileAsync(member.ProfileId)).Username);
        await using var check = fixture.CreateDbContext();
        Assert.False(await check.ReleasedHandles.AnyAsync(r => r.ProfileId == member.ProfileId));
    }

    [Fact]
    public async Task GivenTwoMembers_WhenAskingForTheSameHandleAtOnce_ThenExactlyOneGetsIt()
    {
        var members = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => NewMemberAsync(fixture)));
        var handle = NewHandle();

        var responses = await Task.WhenAll(members.Select(m => ChangeAsync(m, handle)));

        try
        {
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(members.Length - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            await using var db = fixture.CreateDbContext();
            Assert.Equal(1, await db.Profiles.CountAsync(p => p.Username == handle));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task GivenOneMember_WhenChangingTheHandleTwiceAtOnce_ThenOnlyOneChangeHappens()
    {
        var member = await NewMemberAsync(fixture);

        var responses = await Task.WhenAll(ChangeAsync(member, NewHandle()), ChangeAsync(member, NewHandle()));

        try
        {
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task GivenTwoProfilesWithHandlesDifferingInCase_WhenStoringThem_ThenTheDatabaseRefusesTheSecond()
    {
        var name = NewHandle();
        await using var db = fixture.CreateDbContext();
        db.Profiles.Add(new Profile { Username = name, Kind = ProfileKind.Member, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await using var second = fixture.CreateDbContext();
        second.Profiles.Add(new Profile { Username = name.ToUpperInvariant(), Kind = ProfileKind.Member, CreatedAt = DateTimeOffset.UtcNow });

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)failure.InnerException!).SqlState);
    }

    [Fact]
    public async Task GivenAChangedHandle_WhenOpeningTheProfileByIdOrTheLegacyLink_ThenBothResolve()
    {
        var legacyId = 880_000 + Random.Shared.Next(10_000);
        var member = await NewMemberAsync(fixture);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Profiles.Where(p => p.Id == member.ProfileId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LegacyId, legacyId));
        }

        using var change = await ChangeAsync(member, NewHandle());
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        using var byId = await SendAsync(fixture, HttpMethod.Get, $"/api/profiles/{member.ProfileId}");
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        var legacy = await fixture.NoRedirectClient.GetAsync($"/members/{legacyId}");
        Assert.Equal(HttpStatusCode.MovedPermanently, legacy.StatusCode);
        Assert.Equal($"/members/{member.ProfileId}", legacy.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GivenNoToken_WhenChangingTheHandle_ThenTheApiAnswersUnauthorized()
    {
        using var response = await SendAsync(fixture, HttpMethod.Put, Route, token: null, new { handle = NewHandle() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GivenAHandleReservedForAMember_WhenAnotherAccountLogsInForTheFirstTime_ThenItGetsAnotherHandle()
    {
        var former = await NewChosenMemberAsync();
        var released = (await ProfileAsync(former.ProfileId)).Username;
        using var change = await ChangeAsync(former, NewHandle());
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var token = TestTokens.Create(Guid.NewGuid().ToString(), released);

        using var me = await SendAsync(fixture, HttpMethod.Get, "/api/me", token);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var username = (await BodyAsync(me)).GetProperty("profile").GetProperty("username").GetString();
        Assert.Equal($"{released}-2", username);
    }
}
