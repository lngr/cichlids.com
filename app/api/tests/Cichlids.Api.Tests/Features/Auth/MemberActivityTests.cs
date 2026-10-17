// Story: TASK-8.1
using System.Net;
using Cichlids.Api.Features.Auth;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Provisional.ProvisionalTestSupport;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Auth;

/// <summary>
/// The activity of a registered member: the time of the last request within the recording
/// interval and the start of an absence of 28 days or more.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-8.1")]
public sealed class MemberActivityTests(ApiFixture fixture)
{
    private const string AuthenticatedRoute = "/api/me";

    private async Task<Profile> ProfileAsync(long id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    private async Task SetActivityAsync(long id, DateTimeOffset? lastSeenAt, DateTimeOffset? awaySince = null)
    {
        await using var db = fixture.CreateDbContext();
        await db.Profiles.Where(p => p.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.LastSeenAt, lastSeenAt)
            .SetProperty(p => p.AwaySince, awaySince));
    }

    /// <summary>
    /// Cuts the value to the microsecond precision of the database column.
    /// </summary>
    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % 10, value.Offset);

    private async Task<HttpStatusCode> RequestAsync(Member member)
    {
        using var response = await SendAsync(fixture, HttpMethod.Get, AuthenticatedRoute, member.Token);
        return response.StatusCode;
    }

    private async Task RecordAsync(string subject, DateTimeOffset now)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MemberActivityService>().RecordAsync(subject, now, CancellationToken.None);
    }

    private async Task<string> SubjectAsync(long profileId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.ProfileIdentities.Where(i => i.ProfileId == profileId).Select(i => i.Subject).SingleAsync();
    }

    /// <summary>
    /// The transaction id that created the current version of the row; it changes with every write to the row.
    /// </summary>
    private static async Task<string> RowVersionAsync(CichlidsDbContext db, long id) =>
        await db.Database.SqlQuery<string>($"SELECT xmin::text AS \"Value\" FROM public.profile WHERE id = {id}").SingleAsync();

    [Fact]
    public async Task GivenAMemberNeverSeen_WhenTheMemberMakesARequest_ThenLastSeenIsSetAndNoAbsenceIsRecorded()
    {
        var member = await NewMemberAsync(fixture);
        var before = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.OK, await RequestAsync(member));

        var profile = await ProfileAsync(member.ProfileId);
        Assert.InRange(profile.LastSeenAt!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow);
        Assert.Null(profile.AwaySince);
    }

    [Fact]
    public async Task GivenAMemberSeenWithinTheLastHour_WhenTheMemberMakesAnotherRequest_ThenLastSeenStaysUnchangedAndNoWriteHappens()
    {
        var member = await NewMemberAsync(fixture);
        var seen = Truncate(DateTimeOffset.UtcNow.AddMinutes(-59));
        await SetActivityAsync(member.ProfileId, seen);
        await using var db = fixture.CreateDbContext();
        var versionBefore = await RowVersionAsync(db, member.ProfileId);

        Assert.Equal(HttpStatusCode.OK, await RequestAsync(member));

        Assert.Equal(seen, (await ProfileAsync(member.ProfileId)).LastSeenAt);
        Assert.Equal(versionBefore, await RowVersionAsync(db, member.ProfileId));
    }

    [Fact]
    public async Task GivenAMemberSeenMoreThanAnHourAgoWithinTheAbsenceLimit_WhenTheMemberMakesARequest_ThenLastSeenAdvancesAndNoAbsenceIsRecorded()
    {
        var member = await NewMemberAsync(fixture);
        await SetActivityAsync(member.ProfileId, DateTimeOffset.UtcNow.AddDays(-5));

        Assert.Equal(HttpStatusCode.OK, await RequestAsync(member));

        var profile = await ProfileAsync(member.ProfileId);
        Assert.InRange(profile.LastSeenAt!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        Assert.Null(profile.AwaySince);
    }

    [Fact]
    public async Task GivenAMemberLastSeen30DaysAgo_WhenTheMemberMakesARequest_ThenAwaySinceHoldsThePreviousLastSeenAndLastSeenTheTimeOfTheRequest()
    {
        var member = await NewMemberAsync(fixture);
        var previous = Truncate(DateTimeOffset.UtcNow.AddDays(-30));
        await SetActivityAsync(member.ProfileId, previous);

        Assert.Equal(HttpStatusCode.OK, await RequestAsync(member));

        var profile = await ProfileAsync(member.ProfileId);
        Assert.Equal(previous, profile.AwaySince);
        Assert.InRange(profile.LastSeenAt!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GivenAGapOfExactly28Days_WhenRecordingActivity_ThenAnAbsenceStarts()
    {
        var member = await NewMemberAsync(fixture);
        var now = Truncate(DateTimeOffset.UtcNow);
        var previous = now - MemberActivityService.AwayThreshold;
        await SetActivityAsync(member.ProfileId, previous);

        await RecordAsync(await SubjectAsync(member.ProfileId), now);

        var profile = await ProfileAsync(member.ProfileId);
        Assert.Equal(previous, profile.AwaySince);
        Assert.Equal(now, profile.LastSeenAt);
    }

    [Fact]
    public async Task GivenAGapJustBelow28Days_WhenRecordingActivity_ThenNoAbsenceStarts()
    {
        var member = await NewMemberAsync(fixture);
        var now = Truncate(DateTimeOffset.UtcNow);
        await SetActivityAsync(member.ProfileId, now - MemberActivityService.AwayThreshold + TimeSpan.FromTicks(10));

        await RecordAsync(await SubjectAsync(member.ProfileId), now);

        var profile = await ProfileAsync(member.ProfileId);
        Assert.Null(profile.AwaySince);
        Assert.Equal(now, profile.LastSeenAt);
    }

    [Fact]
    public async Task GivenAGapOfExactlyOneHour_WhenRecordingActivity_ThenLastSeenAdvances()
    {
        var member = await NewMemberAsync(fixture);
        var now = Truncate(DateTimeOffset.UtcNow);
        await SetActivityAsync(member.ProfileId, now - MemberActivityService.RecordInterval);

        await RecordAsync(await SubjectAsync(member.ProfileId), now);

        Assert.Equal(now, (await ProfileAsync(member.ProfileId)).LastSeenAt);
    }

    [Fact]
    public async Task GivenAMemberRecordedByThisProcess_WhenTheMemberRequestsAgainWithinTheHour_ThenNoStatementIsSent()
    {
        var member = await NewMemberAsync(fixture);
        Assert.Equal(HttpStatusCode.OK, await RequestAsync(member));

        // A statement of the second request would write this stale time and start an absence.
        var stale = Truncate(DateTimeOffset.UtcNow.AddDays(-40));
        await SetActivityAsync(member.ProfileId, stale);

        Assert.Equal(HttpStatusCode.OK, await RequestAsync(member));

        var profile = await ProfileAsync(member.ProfileId);
        Assert.Equal(stale, profile.LastSeenAt);
        Assert.Null(profile.AwaySince);
    }

    [Fact]
    public async Task GivenAFirstRequestThatCreatesTheProfile_WhenTheMemberRequestsAgain_ThenTheActivityIsRecorded()
    {
        var subject = Guid.NewGuid().ToString();
        var token = TestTokens.Create(subject, $"first-{Guid.NewGuid():N}"[..20]);

        using (await SendAsync(fixture, HttpMethod.Get, AuthenticatedRoute, token)) { }
        using (await SendAsync(fixture, HttpMethod.Get, AuthenticatedRoute, token)) { }

        await using var db = fixture.CreateDbContext();
        var lastSeen = await db.ProfileIdentities.Where(i => i.Subject == subject)
            .Join(db.Profiles, i => i.ProfileId, p => p.Id, (_, p) => p.LastSeenAt).SingleAsync();
        Assert.NotNull(lastSeen);
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenItMakesRequests_ThenItsProfileIsNotTracked()
    {
        using var caller = await NewAsync(fixture.Client);

        await CreateTankAsync(fixture.Client, caller);
        await CreateTankAsync(fixture.Client, caller);

        var profile = await ProfileAsync(caller.ProfileId);
        Assert.Null(profile.LastSeenAt);
        Assert.Null(profile.AwaySince);
    }

    [Fact]
    public async Task GivenAnAnonymousRequestAndAnArchivedProfile_WhenActivityIsRecorded_ThenNothingIsTracked()
    {
        using var anonymous = await SendAsync(fixture, HttpMethod.Get, "/healthz");
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);

        var member = await NewMemberAsync(fixture);
        var subject = await SubjectAsync(member.ProfileId);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Profiles.Where(p => p.Id == member.ProfileId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Kind, ProfileKind.Archived));
        }

        await RecordAsync(subject, DateTimeOffset.UtcNow);

        Assert.Null((await ProfileAsync(member.ProfileId)).LastSeenAt);
    }

    [Fact]
    public async Task GivenAMemberLastSeen30DaysAgo_WhenManyRequestsArriveAtOnce_ThenTheAbsenceStartIsWrittenOnceAndNotOverwritten()
    {
        var member = await NewMemberAsync(fixture);
        var previous = Truncate(DateTimeOffset.UtcNow.AddDays(-30));
        await SetActivityAsync(member.ProfileId, previous);

        var statuses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => RequestAsync(member)));

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
        var profile = await ProfileAsync(member.ProfileId);
        Assert.Equal(previous, profile.AwaySince);
        Assert.InRange(profile.LastSeenAt!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GivenARecordedAbsence_WhenItIsReadAndCleared_ThenTheStartIsReturnedAndThenGone()
    {
        var member = await NewMemberAsync(fixture);
        var start = Truncate(DateTimeOffset.UtcNow.AddDays(-40));
        await SetActivityAsync(member.ProfileId, DateTimeOffset.UtcNow, start);

        await using var scope = fixture.Services.CreateAsyncScope();
        var activity = scope.ServiceProvider.GetRequiredService<MemberActivityService>();

        Assert.Equal(start, await activity.GetAwaySinceAsync(member.ProfileId, CancellationToken.None));
        await activity.ClearAwaySinceAsync(member.ProfileId, CancellationToken.None);
        Assert.Null(await activity.GetAwaySinceAsync(member.ProfileId, CancellationToken.None));
        Assert.Null((await ProfileAsync(member.ProfileId)).AwaySince);
    }

    [Fact]
    public async Task GivenAMemberWithActivity_WhenReadingProfileAndMe_ThenNeitherActivityColumnIsExposed()
    {
        var member = await NewMemberAsync(fixture);
        await SetActivityAsync(member.ProfileId, DateTimeOffset.UtcNow.AddDays(-40), DateTimeOffset.UtcNow.AddDays(-90));

        using var me = await SendAsync(fixture, HttpMethod.Get, AuthenticatedRoute, member.Token);
        using var profile = await SendAsync(fixture, HttpMethod.Get, $"/api/profiles/{member.ProfileId}");

        foreach (var text in new[] { await me.Content.ReadAsStringAsync(), await profile.Content.ReadAsStringAsync() })
        {
            Assert.DoesNotContain("lastSeen", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("awaySince", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
