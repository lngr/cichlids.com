using Cichlids.Etl.Identity;
using Cichlids.Infrastructure.Identity;

namespace Cichlids.Etl.Tests;

public sealed class LegacyAccountPlannerTests
{
    private static readonly GeneratedNames Names = new("planner-test-secret");

    [Fact]
    public void MergesRecordsSharingAnEmailInDifferentCaseAddsUpdatePasswordAndAGoogleLink()
    {
        Auth0ExportRecord[] records =
        [
            new("auth0|1", "Member@Example.com", false, "Username-Password-Authentication"),
            new("google-oauth2|987", "member@example.com", true, "google-oauth2"),
        ];

        var plan = Plan(records);

        var account = Assert.Single(plan.Accounts);
        Assert.Equal(Names.Generate("login:member@example.com"), account.Username);
        Assert.Equal("member@example.com", account.Email);
        Assert.True(account.EmailVerified);
        Assert.Equal(["TERMS_AND_CONDITIONS", "UPDATE_PASSWORD"], account.RequiredActions);
        Assert.Equal([new FederatedIdentityLink("google", "987")], account.FederatedIdentities);
    }

    [Fact]
    public void LeavesOutTheSocialLinkOfAMergedRecordWhoseEmailIsUnverified()
    {
        Auth0ExportRecord[] records =
        [
            new("auth0|1", "member@example.com", true, "Username-Password-Authentication"),
            new("google-oauth2|987", "member@example.com", false, "google-oauth2"),
            new("facebook|654", "Member@example.com", false, "facebook"),
        ];

        var plan = Plan(records);

        var account = Assert.Single(plan.Accounts);
        Assert.True(account.EmailVerified);
        Assert.Empty(account.FederatedIdentities);
    }

    [Fact]
    public void LinksASocialRecordWithoutOwnEmailWhoseLegacySubjectResolvesTheEmail()
    {
        Auth0ExportRecord[] records = [new("facebook|654", null, false, "facebook")];
        LegacyMember[] members = [new(5, "legacy", 0, "legacy@example.com")];

        var plan = Plan(records, members, new Dictionary<string, int> { ["facebook|654"] = 5 });

        Assert.Equal([new FederatedIdentityLink("facebook", "654")], Assert.Single(plan.Accounts).FederatedIdentities);
    }

    [Fact]
    public void ResolvesTheEmailOfAnEmaillessRecordThroughTheLegacyMap()
    {
        Auth0ExportRecord[] records = [new("auth0|1", null, false, "Username-Password-Authentication")];
        LegacyMember[] members = [new(5, "legacy", 0, " Legacy@Example.com ")];

        var plan = Plan(records, members, new Dictionary<string, int> { ["auth0|1"] = 5 });

        var account = Assert.Single(plan.Accounts);
        Assert.Equal("legacy@example.com", account.Email);
    }

    [Fact]
    public void KeepsAnEmaillessRecordWithAMigratedProfileAsAnAccountKeyedByTheAuth0Id()
    {
        Auth0ExportRecord[] records = [new("facebook|1234", null, false, "facebook")];
        var profileIdByAuth0Subject = new Dictionary<string, long> { ["facebook|1234"] = 42 };

        var plan = Plan(records, profileIdByAuth0Subject: profileIdByAuth0Subject);

        var account = Assert.Single(plan.Accounts);
        Assert.Null(account.Email);
        Assert.Equal(Names.Generate("login:facebook|1234"), account.Username);
        Assert.Equal(42, account.ProfileId);
        Assert.Equal([new FederatedIdentityLink("facebook", "1234")], account.FederatedIdentities);
        Assert.Equal(0, plan.DroppedNoContactNoProfile);
    }

    [Fact]
    public void GivesAnEmaillessAuth0AccountWithAProfileNoFederatedIdentity()
    {
        Auth0ExportRecord[] records = [new("auth0|abc", null, false, "Username-Password-Authentication")];
        var profileIdByAuth0Subject = new Dictionary<string, long> { ["auth0|abc"] = 7 };

        var plan = Plan(records, profileIdByAuth0Subject: profileIdByAuth0Subject);

        Assert.Empty(Assert.Single(plan.Accounts).FederatedIdentities);
    }

    [Fact]
    public void DropsAnEmaillessRecordWithoutAMigratedProfileAndCountsIt()
    {
        Auth0ExportRecord[] records = [new("facebook|1234", null, false, "facebook")];

        var plan = Plan(records);

        Assert.Empty(plan.Accounts);
        Assert.Equal(1, plan.DroppedNoContactNoProfile);
    }

    [Fact]
    public void LinksAnAccountToAProfileFoundByEmail()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", false, "Username-Password-Authentication")];
        var profileIdByEmail = new Dictionary<string, long> { ["member@example.com"] = 11 };

        var plan = Plan(records, profileIdByEmail: profileIdByEmail);

        Assert.Equal(11, Assert.Single(plan.Accounts).ProfileId);
    }

    [Fact]
    public void LinksAnAccountToAProfileFoundByAuth0SubjectWhenNoProfileMatchesTheEmail()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", false, "Username-Password-Authentication")];
        var profileIdByAuth0Subject = new Dictionary<string, long> { ["auth0|1"] = 22 };

        var plan = Plan(records, profileIdByAuth0Subject: profileIdByAuth0Subject);

        Assert.Equal(22, Assert.Single(plan.Accounts).ProfileId);
    }

    [Fact]
    public void LeavesProfileIdNullWhenNoProfileMatches()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", false, "Username-Password-Authentication")];

        var plan = Plan(records);

        Assert.Null(Assert.Single(plan.Accounts).ProfileId);
    }

    [Fact]
    public void GivesEveryAccountTermsAndConditionsEvenWithoutAPasswordConnection()
    {
        Auth0ExportRecord[] records = [new("google-oauth2|1", "member@example.com", true, "google-oauth2")];

        var plan = Plan(records);

        Assert.Equal(["TERMS_AND_CONDITIONS"], Assert.Single(plan.Accounts).RequiredActions);
    }

    [Fact]
    public void ProducesNoFederatedIdentityForAnAuth0OnlyAccount()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", false, "Username-Password-Authentication")];

        var plan = Plan(records);

        Assert.Empty(Assert.Single(plan.Accounts).FederatedIdentities);
    }

    [Fact]
    public void AssignsADeterministicIdStableAcrossRepeatedPlanningRuns()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", false, "Username-Password-Authentication")];

        var first = Plan(records);
        var second = Plan(records);

        var expectedId = Uuidv5.Create(LegacyAccountPlanner.NamespaceId, "member@example.com");
        Assert.Equal(expectedId, Assert.Single(first.Accounts).KeycloakUserId);
        Assert.Equal(expectedId, Assert.Single(second.Accounts).KeycloakUserId);
    }

    [Fact]
    public void MatchesTheRfc4122VersionFiveTestVector()
    {
        var dnsNamespace = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        var id = Uuidv5.Create(dnsNamespace, "www.example.com");

        Assert.Equal(Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2"), id);
    }

    [Fact]
    public void KeepsTheLegacyUsernameOfTheRowJoinedByEmail()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "Member@Example.com", true, "Username-Password-Authentication")];
        LegacyMember[] members = [new(10, "Fish_Fan.1", 100, " member@example.COM ")];

        var account = Assert.Single(Plan(records, members).Accounts);

        Assert.Equal("Fish_Fan.1", account.Username);
        Assert.Equal(LoginUsernameSource.Legacy, account.UsernameSource);
        Assert.Equal(Names.Generate("login:member@example.com"), account.GeneratedUsername);
    }

    [Fact]
    public void KeepsTheLegacyUsernameOfAnEmaillessAccountsRowJoinedBySubject()
    {
        Auth0ExportRecord[] records = [new("facebook|9", null, false, "facebook")];
        LegacyMember[] members = [new(9, "erin", 0, "")];

        var plan = Plan(
            records,
            members,
            new Dictionary<string, int> { ["facebook|9"] = 9 },
            profileIdByAuth0Subject: new Dictionary<string, long> { ["facebook|9"] = 42 });

        var account = Assert.Single(plan.Accounts);
        Assert.Equal("erin", account.Username);
        Assert.Equal(Names.Generate("login:facebook|9"), account.GeneratedUsername);
    }

    [Theory]
    [InlineData("bob@example.org", LoginUsernameSource.EmailLike)]
    [InlineData("Aaron Giles", LoginUsernameSource.RejectedByKeycloak)]
    [InlineData("Al", LoginUsernameSource.RejectedByKeycloak)]
    [InlineData("", LoginUsernameSource.RejectedByKeycloak)]
    public void GeneratesTheUsernameWhenTheLegacyUsernameIsUnusable(string legacyUsername, LoginUsernameSource source)
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", true, "Username-Password-Authentication")];
        LegacyMember[] members = [new(10, legacyUsername, 100, "member@example.com")];

        var account = Assert.Single(Plan(records, members).Accounts);

        Assert.Equal(Names.Generate("login:member@example.com"), account.Username);
        Assert.Equal(account.Username, account.GeneratedUsername);
        Assert.Equal(source, account.UsernameSource);
    }

    [Fact]
    public void GeneratesTheUsernameWhenNoLegacyRowBelongsToTheAccount()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", true, "Username-Password-Authentication")];

        var account = Assert.Single(Plan(records).Accounts);

        Assert.Equal(Names.Generate("login:member@example.com"), account.Username);
        Assert.Equal(LoginUsernameSource.NoLegacyRow, account.UsernameSource);
    }

    [Fact]
    public void GivesADuplicateNameToTheAccountWithAMigratedProfile()
    {
        Auth0ExportRecord[] records =
        [
            new("auth0|1", "late@example.com", true, "Username-Password-Authentication"),
            new("auth0|2", "profile@example.com", true, "Username-Password-Authentication"),
        ];
        LegacyMember[] members = [new(1, "Fish", 900, "late@example.com"), new(2, "fish", 100, "profile@example.com")];

        var plan = Plan(records, members, profileIdByEmail: new Dictionary<string, long> { ["profile@example.com"] = 7 });

        Assert.Equal("fish", Username(plan, "profile@example.com"));
        Assert.Equal(Names.Generate("login:late@example.com"), Username(plan, "late@example.com"));
        Assert.Equal(LoginUsernameSource.Duplicate, plan.Accounts.Single(a => a.Email == "late@example.com").UsernameSource);
    }

    [Fact]
    public void GivesADuplicateNameWithoutProfilesToTheLatestLogin()
    {
        Auth0ExportRecord[] records =
        [
            new("auth0|1", "early@example.com", true, "Username-Password-Authentication"),
            new("auth0|2", "late@example.com", true, "Username-Password-Authentication"),
        ];
        LegacyMember[] members = [new(1, "Fish", 100, "early@example.com"), new(2, "FISH", 900, "late@example.com")];

        var plan = Plan(records, members);

        Assert.Equal("FISH", Username(plan, "late@example.com"));
        Assert.Equal(Names.Generate("login:early@example.com"), Username(plan, "early@example.com"));
    }

    [Fact]
    public void GivesADuplicateNameWithEqualLoginsToTheLowestLegacyId()
    {
        Auth0ExportRecord[] records =
        [
            new("auth0|1", "high@example.com", true, "Username-Password-Authentication"),
            new("auth0|2", "low@example.com", true, "Username-Password-Authentication"),
        ];
        LegacyMember[] members = [new(8, "fish", 100, "high@example.com"), new(3, "fish", 100, "low@example.com")];

        var plan = Plan(records, members);

        Assert.Equal("fish", Username(plan, "low@example.com"));
        Assert.Equal(Names.Generate("login:high@example.com"), Username(plan, "high@example.com"));
    }

    [Fact]
    public void PicksTheRowOfTheMigratedProfileAmongRowsSharingTheEmail()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "shared@example.com", true, "Username-Password-Authentication")];
        LegacyMember[] members = [new(4, "profileowner", 100, "shared@example.com"), new(5, "laterlogin", 900, "shared@example.com")];

        var plan = Plan(
            records,
            members,
            profileIdByEmail: new Dictionary<string, long> { ["shared@example.com"] = 70 },
            legacyIdByProfileId: new Dictionary<long, int> { [70] = 4 });

        Assert.Equal("profileowner", Username(plan, "shared@example.com"));
    }

    [Fact]
    public void PicksTheRowWithTheLatestLoginAmongRowsSharingTheEmailWithoutAProfile()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "shared@example.com", true, "Username-Password-Authentication")];
        LegacyMember[] members = [new(4, "earlier", 100, "shared@example.com"), new(5, "laterlogin", 900, "shared@example.com")];

        Assert.Equal("laterlogin", Username(Plan(records, members), "shared@example.com"));
    }

    [Fact]
    public void SkipsAGeneratedCandidateThatAnotherAccountKeepsAsLegacyName()
    {
        var firstCandidate = Names.Generate("login:generated@example.com");
        Auth0ExportRecord[] records =
        [
            new("auth0|1", "generated@example.com", true, "Username-Password-Authentication"),
            new("auth0|2", "legacy@example.com", true, "Username-Password-Authentication"),
        ];
        LegacyMember[] members = [new(2, firstCandidate.ToUpperInvariant(), 100, "legacy@example.com")];

        var plan = Plan(records, members);

        Assert.Equal(firstCandidate.ToUpperInvariant(), Username(plan, "legacy@example.com"));
        Assert.Equal(Names.Generate("login:generated@example.com:1"), Username(plan, "generated@example.com"));
    }

    [Fact]
    public void PlansTheSameUsernamesRegardlessOfTheRecordOrder()
    {
        Auth0ExportRecord[] records =
        [
            new("auth0|1", "a@example.com", true, "Username-Password-Authentication"),
            new("auth0|2", "b@example.com", true, "Username-Password-Authentication"),
            new("auth0|3", "c@example.com", true, "Username-Password-Authentication"),
            new("facebook|4", null, false, "facebook"),
        ];
        LegacyMember[] members =
        [
            new(1, "fish", 100, "a@example.com"), new(2, "Fish", 100, "b@example.com"),
            new(3, "c@example.com", 0, "c@example.com"), new(4, "fourth", 0, ""),
        ];
        var uidBySubject = new Dictionary<string, int> { ["facebook|4"] = 4 };
        var profiles = new Dictionary<string, long> { ["facebook|4"] = 1 };

        var forward = Plan(records, members, uidBySubject, profileIdByAuth0Subject: profiles);
        var backward = Plan(records.Reverse().ToArray(), members.Reverse().ToArray(), uidBySubject, profileIdByAuth0Subject: profiles);

        var expected = forward.Accounts.ToDictionary(a => a.KeycloakUserId, a => (a.Username, a.GeneratedUsername));
        Assert.Equal(expected, backward.Accounts.ToDictionary(a => a.KeycloakUserId, a => (a.Username, a.GeneratedUsername)));
        Assert.Equal("fish", Username(forward, "a@example.com"));
        Assert.Equal("fourth", forward.Accounts.Single(a => a.Email is null).Username);
    }

    private static string Username(AccountPlan plan, string email) => plan.Accounts.Single(a => a.Email == email).Username;

    private static AccountPlan Plan(
        IReadOnlyList<Auth0ExportRecord> records,
        IReadOnlyList<LegacyMember>? members = null,
        Dictionary<string, int>? legacyUidBySubject = null,
        Dictionary<string, long>? profileIdByEmail = null,
        Dictionary<string, long>? profileIdByAuth0Subject = null,
        Dictionary<long, int>? legacyIdByProfileId = null) =>
        LegacyAccountPlanner.Plan(
            new LegacyAccountSources(
                records,
                members ?? [],
                legacyUidBySubject ?? [],
                profileIdByEmail ?? [],
                profileIdByAuth0Subject ?? [],
                legacyIdByProfileId ?? []),
            Names);
}
