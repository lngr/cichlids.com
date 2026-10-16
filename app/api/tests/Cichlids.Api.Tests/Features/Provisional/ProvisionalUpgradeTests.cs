// Story: TASK-7.3
using System.Net;
using Cichlids.Api.Features.Auth.Devices;
using Cichlids.Api.Tests.Features.Auth.Devices;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Provisional.ProvisionalTestSupport;
using static Cichlids.Api.Tests.Features.Provisional.RegistrationTestSupport;

namespace Cichlids.Api.Tests.Features.Provisional;

/// <summary>
/// The upgrade of a provisional profile into the profile of a new Keycloak account: the content stays,
/// the profile becomes a member with its first public handle, the device credential is retired, and
/// the call refuses every request that lacks one of its two proofs.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-7.3")]
public sealed class ProvisionalUpgradeTests(ApiFixture fixture)
{
    [Fact]
    public async Task GivenAProvisionalAccountWithATank_WhenTheMemberRegistersANewAccount_ThenTheTankAndItsCareBelongToTheRegisteredProfile()
    {
        using var caller = await NewAsync(fixture.Client);
        var content = await AddContentAsync(fixture, caller);
        var account = KeycloakAccount.New(name: "Tanja Tank");

        using var response = await PostWithProofAsync(fixture.Client, Upgrade, account.Token, caller.Device);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await BodyAsync(response);
        Assert.Equal(caller.ProfileId, me.GetProperty("profile").GetProperty("id").GetInt64());
        Assert.Equal(account.Username, me.GetProperty("profile").GetProperty("username").GetString());
        Assert.Equal("Tanja Tank", me.GetProperty("profile").GetProperty("displayName").GetString());
        Assert.False(me.GetProperty("provisional").GetBoolean());

        await using var db = fixture.CreateDbContext();
        var profile = await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == caller.ProfileId);
        Assert.Equal(ProfileKind.Member, profile.Kind);
        var identity = await db.ProfileIdentities.AsNoTracking().SingleAsync(i => i.ProfileId == caller.ProfileId);
        Assert.Equal(("oidc", account.Subject), (identity.Provider, identity.Subject));
        var tank = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == content.Tank);
        Assert.Equal(caller.ProfileId, tank.ProfileId);
        Assert.Equal(TankVisibility.Private, tank.ProfileVisibility);
        Assert.Equal(caller.ProfileId, (await db.CareEntries.AsNoTracking().SingleAsync(e => e.Id == content.CareEntry)).AuthorProfileId);
        Assert.Equal(caller.ProfileId, (await db.MediaItems.AsNoTracking().SingleAsync(m => m.Id == content.MediaItem)).OwnerProfileId);
        Assert.Equal(caller.ProfileId, (await db.CareTodos.AsNoTracking().SingleAsync(t => t.Id == content.Todo)).ProfileId);
        Assert.Equal(1, await db.Profiles.CountAsync(p => p.Id == caller.ProfileId));
    }

    [Fact]
    public async Task GivenAnUpgradedAccount_WhenTheKeycloakTokenCallsTheApi_ThenItReachesTheSameProfileAndItsTank()
    {
        using var caller = await NewAsync(fixture.Client);
        var content = await AddContentAsync(fixture, caller);
        var account = KeycloakAccount.New();
        using var upgraded = await PostWithProofAsync(fixture.Client, Upgrade, account.Token, caller.Device);
        Assert.Equal(HttpStatusCode.OK, upgraded.StatusCode);

        using var me = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me", account.Token);
        using var tank = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{content.Tank}", account.Token);
        using var entries = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{content.Tank}/care", account.Token);

        Assert.Equal(caller.ProfileId, (await BodyAsync(me)).GetProperty("profile").GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.OK, tank.StatusCode);
        Assert.Equal(HttpStatusCode.OK, entries.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.ProfileIdentities.CountAsync(i => i.Subject == account.Subject));
    }

    [Fact]
    public async Task GivenAnUpgrade_WhenItSucceeds_ThenTheDeviceCredentialIsRetiredAndTheDeviceTokenAndTheKeyAreRefused()
    {
        using var caller = await NewAsync(fixture.Client);
        var account = KeycloakAccount.New();
        using var upgraded = await PostWithProofAsync(fixture.Client, Upgrade, account.Token, caller.Device);
        Assert.Equal(HttpStatusCode.OK, upgraded.StatusCode);

        await using var db = fixture.CreateDbContext();
        var credential = await db.DeviceCredentials.AsNoTracking().SingleAsync(c => c.Id == caller.Device.CredentialId);
        Assert.Equal(DeviceCredentialState.Retired, credential.State);
        Assert.NotNull(credential.RetiredAt);
        using var oldToken = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me", caller.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, oldToken.StatusCode);
        using var exchange = await caller.Device.SignInAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, exchange.StatusCode);
        using var registration = await fixture.Client.PostAsync("/api/auth/device/credentials",
            System.Net.Http.Json.JsonContent.Create(new { publicKey = caller.Device.PublicKey }));
        await AssertProblemAsync(registration, HttpStatusCode.Conflict, "device_key_retired");
    }

    [Fact]
    public async Task GivenAnUpgrade_WhenItSucceeds_ThenTheOutboxHoldsProfileRegisteredWithoutPrivateContent()
    {
        using var caller = await NewAsync(fixture.Client);
        await AddContentAsync(fixture, caller, "Secret tank title");
        var account = KeycloakAccount.New();

        using var response = await PostWithProofAsync(fixture.Client, Upgrade, account.Token, caller.Device);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = fixture.CreateDbContext();
        var payloads = await EventPayloadsAsync(db, "profile.registered", caller.ProfileId);
        var payload = Assert.Single(payloads);
        Assert.Equal(caller.ProfileId, payload.GetProperty("profileId").GetInt64());
        Assert.False(payload.GetProperty("movedContent").GetBoolean());
        Assert.DoesNotContain("Secret tank title", payload.GetRawText());
        Assert.DoesNotContain(account.Username, payload.GetRawText());
    }

    [Theory]
    [InlineData("someone@example.org")]
    [InlineData("community-archive")]
    public async Task GivenAnUnusableUsername_WhenUpgrading_ThenTheFirstPublicHandleIsGenerated(string username)
    {
        using var caller = await NewAsync(fixture.Client);
        var account = KeycloakAccount.New(username);

        using var response = await PostWithProofAsync(fixture.Client, Upgrade, account.Token, caller.Device);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var handle = (await BodyAsync(response)).GetProperty("profile").GetProperty("username").GetString()!;
        Assert.Matches("^user[1-9][0-9]{7}$", handle);
        var expected = fixture.Services.GetRequiredService<Cichlids.Infrastructure.Identity.GeneratedNames>()
            .Generate(Cichlids.Infrastructure.Identity.GeneratedNames.HandleInput(account.Subject));
        Assert.Equal(expected, handle);
    }

    [Fact]
    public async Task GivenATakenUsername_WhenUpgrading_ThenTheHandleGetsANumericSuffix()
    {
        var taken = $"taken-{Guid.NewGuid():N}"[..20];
        var first = KeycloakAccount.New(taken);
        await MemberProfileIdAsync(fixture, first);
        using var caller = await NewAsync(fixture.Client);
        var account = KeycloakAccount.New(taken);

        using var response = await PostWithProofAsync(fixture.Client, Upgrade, account.Token, caller.Device);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"{taken}-2", (await BodyAsync(response)).GetProperty("profile").GetProperty("username").GetString());
    }

    [Fact]
    public async Task GivenOnlyAKeycloakToken_WhenUpgrading_ThenTheApiRefusesAndTheProfileStaysProvisional()
    {
        using var caller = await NewAsync(fixture.Client);
        var account = KeycloakAccount.New();

        foreach (var body in new object?[] { null, new { }, new { challenge = "x", credentialId = caller.Device.CredentialId, timestamp = 1L, signature = "y" } })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Upgrade);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.Token);
            if (body is not null) request.Content = System.Net.Http.Json.JsonContent.Create(body);
            using var response = await fixture.Client.SendAsync(request);
            await AssertDeviceAuthRefusedAsync(response);
        }

        await AssertUntouchedAsync(caller, account);
    }

    [Fact]
    public async Task GivenOnlyADeviceProof_WhenUpgrading_ThenTheApiAnswersUnauthorizedAndTheProfileStaysProvisional()
    {
        using var caller = await NewAsync(fixture.Client);

        using var response = await PostWithProofAsync(fixture.Client, Upgrade, null, caller.Device);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertUntouchedAsync(caller, null);
    }

    [Fact]
    public async Task GivenTheDeviceTokenAsBearer_WhenUpgrading_ThenTheApiAnswersRegistrationRequired()
    {
        using var caller = await NewAsync(fixture.Client);

        using var response = await PostWithProofAsync(fixture.Client, Upgrade, caller.Token, caller.Device);

        await AssertRegistrationRequiredAsync(response);
        await AssertUntouchedAsync(caller, null);
    }

    [Fact]
    public async Task GivenAUsedChallenge_WhenUpgrading_ThenTheApiRefuses()
    {
        using var caller = await NewAsync(fixture.Client);
        var account = KeycloakAccount.New();
        var existing = KeycloakAccount.New();
        await MemberProfileIdAsync(fixture, existing);
        var proof = await ProofAsync(caller.Device);
        using var firstUse = await SendAsync(fixture.Client, HttpMethod.Post, Upgrade, existing.Token, proof);
        await AssertProblemAsync(firstUse, HttpStatusCode.Conflict, "account_has_profile");

        using var again = await SendAsync(fixture.Client, HttpMethod.Post, Upgrade, account.Token, proof);

        await AssertDeviceAuthRefusedAsync(again);
    }

    [Fact]
    public async Task GivenAWrongSignature_WhenUpgrading_ThenTheApiRefuses()
    {
        using var caller = await NewAsync(fixture.Client);
        using var stranger = new TestDevice(fixture.Client);
        var account = KeycloakAccount.New();
        var challenge = await caller.Device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var forged = new
        {
            challenge = challenge.Challenge,
            credentialId = caller.Device.CredentialId,
            timestamp,
            signature = caller.Device.Sign(challenge.Challenge, caller.Device.CredentialId, timestamp, stranger.Key),
        };

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Upgrade, account.Token, forged);

        await AssertDeviceAuthRefusedAsync(response);
        await AssertUntouchedAsync(caller, account);
    }

    [Fact]
    public async Task GivenAnUpgradedProfile_WhenTheRetiredCredentialSignsAgain_ThenTheApiRefuses()
    {
        using var caller = await NewAsync(fixture.Client);
        using var first = await PostWithProofAsync(fixture.Client, Upgrade, KeycloakAccount.New().Token, caller.Device);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await PostWithProofAsync(fixture.Client, Upgrade, KeycloakAccount.New().Token, caller.Device);

        await AssertDeviceAuthRefusedAsync(second);
    }

    [Fact]
    public async Task GivenACredentialOfAMemberProfile_WhenItsProofIsSent_ThenTheApiRefuses()
    {
        using var caller = await NewAsync(fixture.Client);
        var member = KeycloakAccount.New();
        var memberId = await MemberProfileIdAsync(fixture, member);
        await using (var db = fixture.CreateDbContext())
        {
            await db.DeviceCredentials.Where(c => c.Id == caller.Device.CredentialId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ProfileId, memberId));
        }

        using var response = await PostWithProofAsync(fixture.Client, Upgrade, KeycloakAccount.New().Token, caller.Device);

        await AssertDeviceAuthRefusedAsync(response);
        using var move = await PostWithProofAsync(fixture.Client, Move, member.Token, caller.Device);
        await AssertDeviceAuthRefusedAsync(move);
        await using var check = fixture.CreateDbContext();
        Assert.Equal(ProfileKind.Member, (await check.Profiles.AsNoTracking().SingleAsync(p => p.Id == memberId)).Kind);
    }

    [Fact]
    public async Task GivenAKeycloakAccountWithAProfile_WhenUpgrading_ThenTheApiAnswersAccountHasProfileAndChangesNothing()
    {
        using var caller = await NewAsync(fixture.Client);
        await AddContentAsync(fixture, caller);
        var account = KeycloakAccount.New();
        var memberId = await MemberProfileIdAsync(fixture, account);

        using var response = await PostWithProofAsync(fixture.Client, Upgrade, account.Token, caller.Device);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "account_has_profile");
        await AssertUntouchedAsync(caller, null);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(ProfileKind.Member, (await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == memberId)).Kind);
    }

    [Fact]
    public async Task GivenTwoUpgradesOfOneInstallationInParallel_WhenBothArrive_ThenExactlyOneAccountGetsTheProfile()
    {
        using var caller = await NewAsync(fixture.Client);
        var a = KeycloakAccount.New();
        var b = KeycloakAccount.New();
        var proofA = await ProofAsync(caller.Device);
        var proofB = await ProofAsync(caller.Device);

        var results = await Task.WhenAll(
            SendAsync(fixture.Client, HttpMethod.Post, Upgrade, a.Token, proofA),
            SendAsync(fixture.Client, HttpMethod.Post, Upgrade, b.Token, proofB));

        try
        {
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Forbidden));
            await AssertDeviceAuthRefusedAsync(results.Single(r => r.StatusCode == HttpStatusCode.Forbidden));
            await using var db = fixture.CreateDbContext();
            var identity = await db.ProfileIdentities.AsNoTracking().SingleAsync(i => i.ProfileId == caller.ProfileId);
            Assert.Contains(identity.Subject, new[] { a.Subject, b.Subject });
        }
        finally
        {
            foreach (var result in results) result.Dispose();
        }
    }

    [Fact]
    public async Task GivenTwoInstallationsOfOneAccountInParallel_WhenBothUpgrade_ThenOneUpgradesAndTheOtherMustMove()
    {
        using var first = await NewAsync(fixture.Client);
        using var second = await NewAsync(fixture.Client);
        var account = KeycloakAccount.New();
        var proofFirst = await ProofAsync(first.Device);
        var proofSecond = await ProofAsync(second.Device);

        var results = await Task.WhenAll(
            SendAsync(fixture.Client, HttpMethod.Post, Upgrade, account.Token, proofFirst),
            SendAsync(fixture.Client, HttpMethod.Post, Upgrade, account.Token, proofSecond));

        try
        {
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            await AssertProblemAsync(results.Single(r => r.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "account_has_profile");
            await using var db = fixture.CreateDbContext();
            Assert.Equal(1, await db.ProfileIdentities.CountAsync(i => i.Subject == account.Subject));
        }
        finally
        {
            foreach (var result in results) result.Dispose();
        }
    }

    [Fact]
    public async Task GivenTheFirstCallOfANewAccountAndAnUpgradeInParallel_WhenBothRun_ThenTheAccountHasOneProfile()
    {
        for (var round = 0; round < 6; round++)
        {
            using var caller = await NewAsync(fixture.Client);
            var account = KeycloakAccount.New();
            var proof = await ProofAsync(caller.Device);

            var results = await Task.WhenAll(
                SendAsync(fixture.Client, HttpMethod.Post, Upgrade, account.Token, proof),
                SendAsync(fixture.Client, HttpMethod.Get, "/api/me", account.Token),
                SendAsync(fixture.Client, HttpMethod.Get, "/api/me", account.Token));
            foreach (var result in results) result.Dispose();

            await using var db = fixture.CreateDbContext();
            Assert.Equal(1, await db.ProfileIdentities.CountAsync(i => i.Subject == account.Subject));
            // Whichever call won, the account reaches one profile; when the plain call won, the upgrade answered a conflict.
            var upgraded = results[0].StatusCode == HttpStatusCode.OK;
            Assert.Equal(upgraded, await db.ProfileIdentities.AnyAsync(i => i.Subject == account.Subject && i.ProfileId == caller.ProfileId));
            Assert.True(upgraded || results[0].StatusCode == HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task GivenACleanupThatHoldsTheProfile_WhenAnUpgradeWaitsAndTheCleanupRemovesTheProfile_ThenTheUpgradeIsRefused()
    {
        using var caller = await NewAsync(fixture.Client);
        var account = KeycloakAccount.New();
        var proof = await ProofAsync(caller.Device);

        await using var holder = fixture.CreateDbContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        var held = await holder.Database
            .SqlQuery<long>($"SELECT id AS \"Value\" FROM public.profile WHERE id = {caller.ProfileId} FOR UPDATE").ToListAsync();
        Assert.Single(held);

        var upgrade = SendAsync(fixture.Client, HttpMethod.Post, Upgrade, account.Token, proof);
        await Task.Delay(750);
        Assert.False(upgrade.IsCompleted);
        await holder.Profiles.Where(p => p.Id == caller.ProfileId).ExecuteDeleteAsync();
        await transaction.CommitAsync();

        using var response = await upgrade;
        await AssertDeviceAuthRefusedAsync(response);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Profiles.AnyAsync(p => p.Id == caller.ProfileId));
        Assert.False(await db.ProfileIdentities.AnyAsync(i => i.Subject == account.Subject));
    }

    [Fact]
    public async Task GivenACleanupAndAnUpgradeOfAnAbandonedProfileInParallel_WhenBothRun_ThenTheProfileIsRemovedOrKeptAsAMember()
    {
        for (var round = 0; round < 5; round++)
        {
            using var caller = await NewAsync(fixture.Client);
            await AddContentAsync(fixture, caller);
            var account = KeycloakAccount.New();
            var proof = await ProofAsync(caller.Device);
            await using (var db = fixture.CreateDbContext())
            {
                await db.DeviceCredentials.Where(c => c.ProfileId == caller.ProfileId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSignedRequestAt, DateTimeOffset.UtcNow.AddMonths(-13)));
            }

            using var scope = fixture.Services.CreateScope();
            var cleanup = scope.ServiceProvider.GetRequiredService<ProvisionalAccountCleanupService>().RunOnceAsync(CancellationToken.None);
            var upgrade = SendAsync(fixture.Client, HttpMethod.Post, Upgrade, account.Token, proof);
            await Task.WhenAll(cleanup, upgrade);
            using var response = await upgrade;

            await using var check = fixture.CreateDbContext();
            var profile = await check.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == caller.ProfileId);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal(ProfileKind.Member, profile!.Kind);
                Assert.True(await check.Tanks.AnyAsync(t => t.ProfileId == caller.ProfileId));
            }
            else
            {
                await AssertDeviceAuthRefusedAsync(response);
                Assert.Null(profile);
                Assert.False(await check.Tanks.AnyAsync(t => t.ProfileId == caller.ProfileId));
                Assert.False(await check.ProfileIdentities.AnyAsync(i => i.Subject == account.Subject));
            }
        }
    }

    private async Task AssertUntouchedAsync(ProvisionalCaller caller, KeycloakAccount? account)
    {
        await using var db = fixture.CreateDbContext();
        var profile = await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == caller.ProfileId);
        Assert.Equal(ProfileKind.Provisional, profile.Kind);
        Assert.Equal(DeviceCredentialState.Active, (await db.DeviceCredentials.AsNoTracking().SingleAsync(c => c.Id == caller.Device.CredentialId)).State);
        if (account is not null)
        {
            Assert.False(await db.ProfileIdentities.AnyAsync(i => i.Subject == account.Subject));
        }
    }
}
