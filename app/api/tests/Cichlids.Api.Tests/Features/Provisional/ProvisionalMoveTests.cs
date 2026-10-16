// Story: TASK-7.3
using System.Net;
using Cichlids.Api.Tests.Features.Auth.Devices;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Streaks.StreakTestSupport;
using Visibility = Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;
using static Cichlids.Api.Tests.Features.Provisional.ProvisionalTestSupport;
using static Cichlids.Api.Tests.Features.Provisional.RegistrationTestSupport;

namespace Cichlids.Api.Tests.Features.Provisional;

/// <summary>
/// The preview and the move of the content of a provisional profile into the profile of an existing
/// Keycloak account: what moves, how vacations and checksums merge, what stays with the member, and
/// the refusals of a request that lacks one of its two proofs.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-7.3")]
public sealed class ProvisionalMoveTests(ApiFixture fixture)
{
    private const string Query = "?date=2026-10-15";

    private sealed record Setup(ProvisionalCaller Caller, Content Content, KeycloakAccount Account, long MemberId, long MemberTank, string SharedChecksum);

    /// <summary>
    /// A provisional profile with a tank and everything that follows the tank or the profile, and a
    /// member with a tank, a vacation that overlaps one of the provisional ones and a used checksum
    /// the provisional profile used as well.
    /// </summary>
    private async Task<Setup> ArrangeAsync()
    {
        var caller = await NewAsync(fixture.Client);
        var content = await AddContentAsync(fixture, caller);
        using (var settings = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/settings", caller.Token, new { streaksEnabled = false }))
        {
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        }

        using (var current = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{content.Tank}/water-changes", caller.Token))
        {
            Assert.Equal(HttpStatusCode.Created, current.StatusCode);
        }

        await AddVacationAsync(fixture, caller.Token, "2026-09-01", "2026-09-05");
        await AddVacationAsync(fixture, caller.Token, "2026-08-01", "2026-08-02");
        using (var active = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/vacations{Query}", caller.Token, new { startDate = "2026-10-01" }))
        {
            Assert.Equal(HttpStatusCode.Created, active.StatusCode);
        }

        var shared = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var own = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using (var db = fixture.CreateDbContext())
        {
            db.CareReminderSnoozes.Add(new CareReminderSnooze
            {
                ProfileId = caller.ProfileId, TankId = content.Tank, Kind = CareKind.WaterChange,
                DueDate = new DateOnly(2026, 10, 14), UntilDate = new DateOnly(2026, 10, 16), CreatedAt = DateTimeOffset.UtcNow,
            });
            db.CarePlanRuleChanges.Add(new CarePlanRuleChange { TankId = content.Tank, Kind = CareKind.WaterChange, RhythmDays = 7, Enabled = true, At = DateTimeOffset.UtcNow });
            db.StreakFreezes.Add(new StreakFreeze
            {
                TankId = content.Tank, CareEntryId = content.CareEntry, TankYear = 1,
                PeriodStart = new DateOnly(2026, 10, 1), PeriodEnd = new DateOnly(2026, 10, 7), CreatedAt = DateTimeOffset.UtcNow,
            });
            db.TankDecommissionings.Add(new TankDecommissioning
            {
                TankId = content.Tank, Scope = DecommissionScope.WaterChange, StartedAt = DateTimeOffset.UtcNow,
                StartDate = new DateOnly(2026, 9, 20), StartMediaItemId = content.MediaItem,
            });
            db.UsedChecksums.Add(new UsedChecksum { ProfileId = caller.ProfileId, ChecksumSha256 = shared, Kind = UsedChecksumKind.Proof, FirstUsedAt = DateTimeOffset.UtcNow });
            db.UsedChecksums.Add(new UsedChecksum { ProfileId = caller.ProfileId, ChecksumSha256 = own, Kind = UsedChecksumKind.Proof, FirstUsedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var account = KeycloakAccount.New();
        var memberId = await MemberProfileIdAsync(fixture, account);
        var memberTank = await CreateTankAsync(fixture.Client, account.Token, isPublic: false);
        await AddVacationAsync(fixture, account.Token, "2026-09-03", "2026-09-10");
        await using (var db = fixture.CreateDbContext())
        {
            db.UsedChecksums.Add(new UsedChecksum { ProfileId = memberId, ChecksumSha256 = shared, Kind = UsedChecksumKind.Proof, FirstUsedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });
            await db.SaveChangesAsync();
        }

        return new Setup(caller, content, account, memberId, memberTank, shared);
    }

    [Fact]
    public async Task GivenAProvisionalAccountWithATankAndAnExistingAccount_WhenTheMemberAgreesToTakeTheContentAlong_ThenTheTankBelongsToTheExistingProfileAndTheProvisionalProfileIsGone()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;

        using var response = await PostWithProofAsync(fixture.Client, Move, setup.Account.Token, setup.Caller.Device);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(setup.MemberId, body.GetProperty("me").GetProperty("profile").GetProperty("id").GetInt64());
        var moved = body.GetProperty("moved");
        Assert.Equal(1, moved.GetProperty("tanks").GetInt32());
        Assert.Equal(1, moved.GetProperty("careEntries").GetInt32());
        Assert.Equal(1, moved.GetProperty("mediaItems").GetInt32());
        Assert.Equal(1, moved.GetProperty("todos").GetInt32());
        Assert.Equal(2, moved.GetProperty("vacations").GetInt32());
        Assert.Equal(1, body.GetProperty("vacationsDropped").GetInt32());

        await using var db = fixture.CreateDbContext();
        var from = setup.Caller.ProfileId;
        Assert.False(await db.Profiles.AnyAsync(p => p.Id == from));
        Assert.Equal(setup.MemberId, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == setup.Content.Tank)).ProfileId);
        Assert.Equal(TankVisibility.Private, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == setup.Content.Tank)).ProfileVisibility);
        Assert.Equal(setup.MemberId, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == setup.MemberTank)).ProfileId);
        Assert.Equal(setup.MemberId, (await db.CareEntries.AsNoTracking().SingleAsync(e => e.Id == setup.Content.CareEntry)).AuthorProfileId);
        Assert.Equal(setup.MemberId, (await db.MediaItems.AsNoTracking().SingleAsync(m => m.Id == setup.Content.MediaItem)).OwnerProfileId);
        Assert.True(await db.MediaLinks.AnyAsync(l => l.CareEntryId == setup.Content.CareEntry && l.MediaItemId == setup.Content.MediaItem));
        Assert.Equal(setup.MemberId, (await db.CareTodos.AsNoTracking().SingleAsync(t => t.Id == setup.Content.Todo)).ProfileId);
        Assert.Equal(setup.MemberId, (await db.CareReminderSnoozes.AsNoTracking().SingleAsync(s => s.TankId == setup.Content.Tank)).ProfileId);
        Assert.Equal(1, await db.WaterChangeSessions.CountAsync(s => s.TankId == setup.Content.Tank && s.ProfileId == setup.MemberId));
        Assert.Equal(0, await db.WaterChangeSessions.CountAsync(s => s.ProfileId == from));
    }

    [Fact]
    public async Task GivenAMove_WhenItSucceeds_ThenStreakFreezeAndPlanDataFollowTheTank()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;

        using var response = await PostWithProofAsync(fixture.Client, Move, setup.Account.Token, setup.Caller.Device);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.StreakFreezes.CountAsync(f => f.TankId == setup.Content.Tank && f.CareEntryId == setup.Content.CareEntry));
        Assert.Equal(1, await db.CarePlanRuleChanges.CountAsync(c => c.TankId == setup.Content.Tank));
        Assert.Equal(1, await db.TankDecommissionings.CountAsync(d => d.TankId == setup.Content.Tank && d.EndedAt == null));
        using var streaks = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/streaks{Query}", setup.Account.Token);
        Assert.Equal(HttpStatusCode.OK, streaks.StatusCode);
        var tanks = (await BodyAsync(streaks)).GetProperty("tanks").EnumerateArray().Select(t => t.GetProperty("tankId").GetInt64()).ToList();
        Assert.Contains(setup.Content.Tank, tanks);
        Assert.Contains(setup.MemberTank, tanks);
        using var tankStreaks = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{setup.Content.Tank}/streaks{Query}", setup.Account.Token);
        Assert.Equal(HttpStatusCode.OK, tankStreaks.StatusCode);
    }

    [Fact]
    public async Task GivenVacationsOnBothSides_WhenMoving_ThenNonOverlappingOnesMoveAndOverlappingOnesAreDropped()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;

        using var response = await PostWithProofAsync(fixture.Client, Move, setup.Account.Token, setup.Caller.Device);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = fixture.CreateDbContext();
        var vacations = await db.Vacations.AsNoTracking().Where(v => v.ProfileId == setup.MemberId).OrderBy(v => v.StartDate).ToListAsync();
        Assert.Equal(
            [("2026-08-01", "2026-08-02"), ("2026-09-03", "2026-09-10"), ("2026-10-01", null)],
            vacations.Select(v => (v.StartDate.ToString("yyyy-MM-dd"), v.EndDate?.ToString("yyyy-MM-dd"))));
        Assert.False(await db.Vacations.AnyAsync(v => v.ProfileId == setup.Caller.ProfileId));
    }

    /// <summary>A member whose tank has a broken water change period that a vacation from 10 September to 9 October would repair.</summary>
    private async Task<(Visibility.Member Member, long Tank)> MemberWithBrokenPeriodAsync()
    {
        var member = await Visibility.NewMemberAsync(fixture);
        var tank = await Visibility.NewTankAsync(fixture, member.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, member.ProfileId, At(9, 1), true);
        await AddWaterChangeAsync(fixture, tank, member.ProfileId, At(9, 8), true);
        await AddWaterChangeAsync(fixture, tank, member.ProfileId, At(10, 12), true);
        return (member, tank);
    }

    [Fact]
    public async Task GivenAProvisionalVacationThatWouldRepairAPeriodOfTheMembersTank_WhenMoving_ThenItIsDroppedAndCountedSeparatelyWhileRecentAndHarmlessOnesMove()
    {
        var (member, _) = await MemberWithBrokenPeriodAsync();
        using var caller = await NewAsync(fixture.Client);
        await AddVacationAsync(fixture, caller.Token, "2026-09-10", "2026-10-09");
        await AddVacationAsync(fixture, caller.Token, "2026-08-01", "2026-08-10");
        using (var recent = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/vacations{Query}", caller.Token, new { startDate = "2026-10-14" }))
        {
            Assert.Equal(HttpStatusCode.Created, recent.StatusCode);
        }

        using var preview = await PostWithProofAsync(fixture.Client, Preview, member.Token, caller.Device);
        var previewBody = await BodyAsync(preview);
        Assert.Equal(0, previewBody.GetProperty("vacationsDropped").GetInt32());
        Assert.Equal(1, previewBody.GetProperty("vacationsDroppedRepair").GetInt32());
        Assert.Equal(2, previewBody.GetProperty("content").GetProperty("vacations").GetInt32());

        using var response = await PostWithProofAsync(fixture.Client, Move, member.Token, caller.Device);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(0, body.GetProperty("vacationsDropped").GetInt32());
        Assert.Equal(1, body.GetProperty("vacationsDroppedRepair").GetInt32());
        Assert.Equal(2, body.GetProperty("moved").GetProperty("vacations").GetInt32());
        await using var db = fixture.CreateDbContext();
        var starts = await db.Vacations.AsNoTracking().Where(v => v.ProfileId == member.ProfileId).OrderBy(v => v.StartDate)
            .Select(v => v.StartDate.ToString("yyyy-MM-dd")).ToListAsync();
        Assert.Equal(["2026-08-01", "2026-10-14"], starts);
        Assert.False(await db.Vacations.AnyAsync(v => v.ProfileId == caller.ProfileId));
    }

    [Fact]
    public async Task GivenAVacationEnteredForTheMovedTank_WhenItWouldRepairThatTanksPeriod_ThenItMovesBecauseTheMembersOwnTanksAreUnaffected()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await Visibility.NewTankAsync(fixture, caller.ProfileId, TankVisibility.Private, TankVisibility.Private);
        await AddWaterChangeAsync(fixture, tank, caller.ProfileId, At(9, 1), true);
        await AddWaterChangeAsync(fixture, tank, caller.ProfileId, At(9, 8), true);
        await AddWaterChangeAsync(fixture, tank, caller.ProfileId, At(10, 12), true);
        // Entered while the vacation did not repair anything; written directly because the route would refuse it now.
        await Streaks.StreakTestSupport.AddVacationAsync(fixture, caller.ProfileId, new DateOnly(2026, 9, 10), new DateOnly(2026, 10, 9));
        var account = KeycloakAccount.New();
        var memberId = await MemberProfileIdAsync(fixture, account);

        using var response = await PostWithProofAsync(fixture.Client, Move, account.Token, caller.Device);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await BodyAsync(response)).GetProperty("vacationsDroppedRepair").GetInt32());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Vacations.CountAsync(v => v.ProfileId == memberId && v.StartDate == new DateOnly(2026, 9, 10)));
        using var streaks = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/streaks{Query}", account.Token);
        Assert.Equal(HttpStatusCode.OK, streaks.StatusCode);
        Assert.Single((await BodyAsync(streaks)).GetProperty("vacations").EnumerateArray());
    }

    [Fact]
    public async Task GivenAMoveWithoutALocalDate_WhenTheRequestArrives_ThenTheApiAnswersBadRequestAndKeepsTheProof()
    {
        using var caller = await NewAsync(fixture.Client);
        var account = KeycloakAccount.New();
        await MemberProfileIdAsync(fixture, account);

        using var response = await PostWithProofAsync(fixture.Client, "/api/me/provisional/move", account.Token, caller.Device);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "streak_date_invalid");
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Profiles.AnyAsync(p => p.Id == caller.ProfileId && p.Kind == ProfileKind.Provisional));
    }

    [Fact]
    public async Task GivenAnActiveVacationOnBothSides_WhenMoving_ThenTheProvisionalOneIsDropped()
    {
        using var caller = await NewAsync(fixture.Client);
        await CreateTankAsync(fixture.Client, caller);
        using (var active = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/vacations{Query}", caller.Token, new { startDate = "2026-10-01" }))
        {
            Assert.Equal(HttpStatusCode.Created, active.StatusCode);
        }

        var account = KeycloakAccount.New();
        var memberId = await MemberProfileIdAsync(fixture, account);
        using (var active = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/vacations{Query}", account.Token, new { startDate = "2026-09-20" }))
        {
            Assert.Equal(HttpStatusCode.Created, active.StatusCode);
        }

        using var response = await PostWithProofAsync(fixture.Client, Move, account.Token, caller.Device);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await BodyAsync(response)).GetProperty("vacationsDropped").GetInt32());
        await using var db = fixture.CreateDbContext();
        var vacation = await db.Vacations.AsNoTracking().SingleAsync(v => v.ProfileId == memberId);
        Assert.Equal(new DateOnly(2026, 9, 20), vacation.StartDate);
    }

    [Fact]
    public async Task GivenAMove_WhenItSucceeds_ThenChecksumsMergeAndTheSettingsOfTheMemberStay()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;
        var before = await ReadMemberAsync(setup.MemberId);

        using var response = await PostWithProofAsync(fixture.Client, Move, setup.Account.Token, setup.Caller.Device);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = fixture.CreateDbContext();
        var rows = await db.UsedChecksums.AsNoTracking().Where(c => c.ProfileId == setup.MemberId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), rows.Single(r => r.ChecksumSha256 == setup.SharedChecksum).FirstUsedAt);
        Assert.False(await db.UsedChecksums.AnyAsync(c => c.ProfileId == setup.Caller.ProfileId));
        var after = await ReadMemberAsync(setup.MemberId);
        Assert.Equal(before, after);
        Assert.True(after.StreaksEnabled);
    }

    [Fact]
    public async Task GivenAMove_WhenItSucceeds_ThenTheCredentialStaysRetiredAndTheOutboxHoldsOnlyCounts()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;

        using var response = await PostWithProofAsync(fixture.Client, Move, setup.Account.Token, setup.Caller.Device);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = fixture.CreateDbContext();
        var credential = await db.DeviceCredentials.AsNoTracking().SingleAsync(c => c.Id == setup.Caller.Device.CredentialId);
        Assert.Equal(DeviceCredentialState.Retired, credential.State);
        Assert.Equal(setup.MemberId, credential.ProfileId);
        using var registration = await fixture.Client.PostAsync("/api/auth/device/credentials",
            System.Net.Http.Json.JsonContent.Create(new { publicKey = setup.Caller.Device.PublicKey }));
        await AssertProblemAsync(registration, HttpStatusCode.Conflict, "device_key_retired");
        var payload = Assert.Single(await EventPayloadsAsync(db, "profile.provisional_content_moved", setup.MemberId));
        Assert.Equal(1, payload.GetProperty("tanks").GetInt32());
        Assert.Equal(1, payload.GetProperty("vacationsDropped").GetInt32());
        Assert.DoesNotContain("Provisional tank", payload.GetRawText());
        using var oldToken = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me", setup.Caller.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, oldToken.StatusCode);
    }

    [Fact]
    public async Task GivenAMove_WhenItSucceeds_ThenTheMovedMediaItemsGetANewVisibilityGeneration()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;
        await using var db = fixture.CreateDbContext();
        var generation = (await db.MediaItems.AsNoTracking().SingleAsync(m => m.Id == setup.Content.MediaItem)).VisibilityGeneration;

        using var response = await PostWithProofAsync(fixture.Client, Move, setup.Account.Token, setup.Caller.Device);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var item = await db.MediaItems.AsNoTracking().SingleAsync(m => m.Id == setup.Content.MediaItem);
        Assert.NotEqual(generation, item.VisibilityGeneration);
        Assert.True(item.PlacementPending);
    }

    [Fact]
    public async Task GivenAProvisionalAccountAndAnExistingAccount_WhenPreviewing_ThenTheApiCountsWhatWouldMoveAndChangesNothing()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;

        using var response = await PostWithProofAsync(fixture.Client, Preview, setup.Account.Token, setup.Caller.Device);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("move", body.GetProperty("mode").GetString());
        Assert.Equal(1, body.GetProperty("content").GetProperty("tanks").GetInt32());
        Assert.Equal(2, body.GetProperty("content").GetProperty("vacations").GetInt32());
        Assert.Equal(1, body.GetProperty("vacationsDropped").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("targetDisplayName").GetString()));
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Profiles.AnyAsync(p => p.Id == setup.Caller.ProfileId && p.Kind == ProfileKind.Provisional));
        Assert.Equal(setup.Caller.ProfileId, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == setup.Content.Tank)).ProfileId);
    }

    [Fact]
    public async Task GivenAKeycloakAccountWithoutAProfile_WhenPreviewing_ThenTheModeIsUpgradeAndNoProfileIsCreated()
    {
        using var caller = await NewAsync(fixture.Client);
        await AddContentAsync(fixture, caller);
        var account = KeycloakAccount.New();
        await using var db = fixture.CreateDbContext();
        var profiles = await db.Profiles.CountAsync();

        using var response = await PostWithProofAsync(fixture.Client, Preview, account.Token, caller.Device);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("upgrade", body.GetProperty("mode").GetString());
        Assert.Equal(1, body.GetProperty("content").GetProperty("tanks").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("targetDisplayName").ValueKind);
        Assert.Equal(profiles, await db.Profiles.CountAsync());
        Assert.False(await db.ProfileIdentities.AnyAsync(i => i.Subject == account.Subject));
    }

    [Fact]
    public async Task GivenOnlyADeviceProofOrOnlyAKeycloakToken_WhenMoving_ThenTheApiRefusesAndNothingMoves()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;

        using var onlyProof = await PostWithProofAsync(fixture.Client, Move, null, setup.Caller.Device);
        using var onlyDeviceToken = await PostWithProofAsync(fixture.Client, Move, setup.Caller.Token, setup.Caller.Device);
        using var onlyKeycloak = await SendAsync(fixture.Client, HttpMethod.Post, Move, setup.Account.Token, new { });
        using var noBody = await SendAsync(fixture.Client, HttpMethod.Post, Move, setup.Account.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, onlyProof.StatusCode);
        await AssertRegistrationRequiredAsync(onlyDeviceToken);
        await AssertDeviceAuthRefusedAsync(onlyKeycloak);
        await AssertDeviceAuthRefusedAsync(noBody);
        await AssertNothingMovedAsync(setup);
    }

    [Fact]
    public async Task GivenAProofOfAnotherInstallation_WhenMoving_ThenTheApiMovesOnlyThatInstallationsContent()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;
        using var other = await NewAsync(fixture.Client);
        var otherContent = await AddContentAsync(fixture, other, "Other tank");

        using var response = await PostWithProofAsync(fixture.Client, Move, setup.Account.Token, other.Device);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(setup.MemberId, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == otherContent.Tank)).ProfileId);
        Assert.Equal(setup.Caller.ProfileId, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == setup.Content.Tank)).ProfileId);
    }

    [Fact]
    public async Task GivenAWrongSignatureOrAUsedChallenge_WhenMoving_ThenTheApiRefusesAndNothingMoves()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;
        using var stranger = new TestDevice(fixture.Client);
        var challenge = await setup.Caller.Device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var forged = new
        {
            challenge = challenge.Challenge,
            credentialId = setup.Caller.Device.CredentialId,
            timestamp,
            signature = setup.Caller.Device.Sign(challenge.Challenge, setup.Caller.Device.CredentialId, timestamp, stranger.Key),
        };
        var proof = await ProofAsync(setup.Caller.Device);
        using var first = await SendAsync(fixture.Client, HttpMethod.Post, Move, KeycloakAccount.New().Token, proof);
        await AssertProblemAsync(first, HttpStatusCode.Conflict, "account_has_no_profile");

        using var wrong = await SendAsync(fixture.Client, HttpMethod.Post, Move, setup.Account.Token, forged);
        using var reused = await SendAsync(fixture.Client, HttpMethod.Post, Move, setup.Account.Token, proof);

        await AssertDeviceAuthRefusedAsync(wrong);
        await AssertDeviceAuthRefusedAsync(reused);
        await AssertNothingMovedAsync(setup);
    }

    [Fact]
    public async Task GivenAKeycloakAccountWithoutAProfile_WhenMoving_ThenTheApiAnswersAccountHasNoProfileAndCreatesNothing()
    {
        using var caller = await NewAsync(fixture.Client);
        await AddContentAsync(fixture, caller);
        var account = KeycloakAccount.New();
        await using var db = fixture.CreateDbContext();
        var profiles = await db.Profiles.CountAsync();

        using var response = await PostWithProofAsync(fixture.Client, Move, account.Token, caller.Device);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "account_has_no_profile");
        Assert.Equal(profiles, await db.Profiles.CountAsync());
        Assert.True(await db.Profiles.AnyAsync(p => p.Id == caller.ProfileId && p.Kind == ProfileKind.Provisional));
    }

    [Fact]
    public async Task GivenASuspendedAccount_WhenMoving_ThenTheApiRefusesAndNothingMoves()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;
        await using (var db = fixture.CreateDbContext())
        {
            await db.Profiles.Where(p => p.Id == setup.MemberId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.SuspendedUntil, DateTimeOffset.UtcNow.AddDays(3)));
        }

        using var response = await PostWithProofAsync(fixture.Client, Move, setup.Account.Token, setup.Caller.Device);
        using var preview = await PostWithProofAsync(fixture.Client, Preview, setup.Account.Token, setup.Caller.Device);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "account_suspended");
        await AssertProblemAsync(preview, HttpStatusCode.Forbidden, "account_suspended");
        await AssertNothingMovedAsync(setup);
    }

    [Fact]
    public async Task GivenAnAccountBoundToANonMemberProfile_WhenMoving_ThenTheApiRefusesWithNonMemberProfile()
    {
        using var caller = await NewAsync(fixture.Client);
        var account = KeycloakAccount.New();
        var memberId = await MemberProfileIdAsync(fixture, account);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Profiles.Where(p => p.Id == memberId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Kind, ProfileKind.Archived));
        }

        using var response = await PostWithProofAsync(fixture.Client, Move, account.Token, caller.Device);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "non_member_profile");
    }

    [Fact]
    public async Task GivenTwoMovesOfOneInstallationInParallel_WhenBothArrive_ThenExactlyOneMoves()
    {
        var setup = await ArrangeAsync();
        using var _ = setup.Caller;
        var proofA = await ProofAsync(setup.Caller.Device);
        var proofB = await ProofAsync(setup.Caller.Device);

        var results = await Task.WhenAll(
            SendAsync(fixture.Client, HttpMethod.Post, Move, setup.Account.Token, proofA),
            SendAsync(fixture.Client, HttpMethod.Post, Move, setup.Account.Token, proofB));

        try
        {
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            await AssertDeviceAuthRefusedAsync(results.Single(r => r.StatusCode == HttpStatusCode.Forbidden));
            await using var db = fixture.CreateDbContext();
            Assert.Equal(setup.MemberId, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == setup.Content.Tank)).ProfileId);
        }
        finally
        {
            foreach (var result in results) result.Dispose();
        }
    }

    [Fact]
    public async Task GivenTwoInstallationsMovingIntoOneAccountInParallel_WhenBothArrive_ThenBothMoveAndOneActiveVacationRemains()
    {
        var account = KeycloakAccount.New();
        var memberId = await MemberProfileIdAsync(fixture, account);
        using var first = await NewAsync(fixture.Client);
        using var second = await NewAsync(fixture.Client);
        foreach (var caller in new[] { first, second })
        {
            await AddContentAsync(fixture, caller);
            using var active = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/vacations{Query}", caller.Token, new { startDate = "2026-10-01" });
            Assert.Equal(HttpStatusCode.Created, active.StatusCode);
        }

        var proofFirst = await ProofAsync(first.Device);
        var proofSecond = await ProofAsync(second.Device);
        var results = await Task.WhenAll(
            SendAsync(fixture.Client, HttpMethod.Post, Move, account.Token, proofFirst),
            SendAsync(fixture.Client, HttpMethod.Post, Move, account.Token, proofSecond));

        try
        {
            Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            await using var db = fixture.CreateDbContext();
            Assert.Equal(2, await db.Tanks.CountAsync(t => t.ProfileId == memberId));
            Assert.Equal(1, await db.Vacations.CountAsync(v => v.ProfileId == memberId && v.EndDate == null));
        }
        finally
        {
            foreach (var result in results) result.Dispose();
        }
    }

    [Fact]
    public async Task GivenACleanupThatHoldsTheProfile_WhenAMoveWaitsAndTheCleanupRemovesTheProfile_ThenTheMoveIsRefused()
    {
        using var caller = await NewAsync(fixture.Client);
        var account = KeycloakAccount.New();
        var memberId = await MemberProfileIdAsync(fixture, account);
        var proof = await ProofAsync(caller.Device);

        await using var holder = fixture.CreateDbContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        var held = await holder.Database
            .SqlQuery<long>($"SELECT id AS \"Value\" FROM public.profile WHERE id = {caller.ProfileId} FOR UPDATE").ToListAsync();
        Assert.Single(held);

        var move = SendAsync(fixture.Client, HttpMethod.Post, Move, account.Token, proof);
        await Task.Delay(750);
        Assert.False(move.IsCompleted);
        await holder.Profiles.Where(p => p.Id == caller.ProfileId).ExecuteDeleteAsync();
        await transaction.CommitAsync();

        using var response = await move;
        await AssertDeviceAuthRefusedAsync(response);
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Profiles.AnyAsync(p => p.Id == memberId && p.Kind == ProfileKind.Member));
    }

    private sealed record MemberSnapshot(string Username, string? DisplayName, bool StreaksEnabled, ProfileKind Kind);

    private async Task<MemberSnapshot> ReadMemberAsync(long profileId)
    {
        await using var db = fixture.CreateDbContext();
        var p = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == profileId);
        return new MemberSnapshot(p.Username, p.DisplayName, p.StreaksEnabled, p.Kind);
    }

    private async Task AssertNothingMovedAsync(Setup setup)
    {
        await using var db = fixture.CreateDbContext();
        Assert.Equal(setup.Caller.ProfileId, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == setup.Content.Tank)).ProfileId);
        Assert.Equal(setup.Caller.ProfileId, (await db.MediaItems.AsNoTracking().SingleAsync(m => m.Id == setup.Content.MediaItem)).OwnerProfileId);
        Assert.Equal(3, await db.Vacations.CountAsync(v => v.ProfileId == setup.Caller.ProfileId));
        Assert.Equal(1, await db.Vacations.CountAsync(v => v.ProfileId == setup.MemberId));
        Assert.Equal(DeviceCredentialState.Active, (await db.DeviceCredentials.AsNoTracking().SingleAsync(c => c.Id == setup.Caller.Device.CredentialId)).State);
        Assert.True(await db.Profiles.AnyAsync(p => p.Id == setup.Caller.ProfileId && p.Kind == ProfileKind.Provisional));
    }
}
