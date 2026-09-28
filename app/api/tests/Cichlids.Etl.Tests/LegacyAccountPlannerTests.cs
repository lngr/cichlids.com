using Cichlids.Etl.Identity;

namespace Cichlids.Etl.Tests;

public sealed class LegacyAccountPlannerTests
{
    private static readonly Dictionary<string, string> NoLegacyEmails = [];
    private static readonly Dictionary<string, long> NoProfilesByEmail = [];
    private static readonly Dictionary<string, long> NoProfilesByAuth0Subject = [];

    [Fact]
    public void MergesRecordsSharingAnEmailInDifferentCaseAddsUpdatePasswordAndAGoogleLink()
    {
        Auth0ExportRecord[] records =
        [
            new("auth0|1", "Member@Example.com", false, "Username-Password-Authentication"),
            new("google-oauth2|987", "member@example.com", true, "google-oauth2"),
        ];

        var plan = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, NoProfilesByAuth0Subject);

        var account = Assert.Single(plan.Accounts);
        Assert.Equal("member@example.com", account.Username);
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

        var plan = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, NoProfilesByAuth0Subject);

        var account = Assert.Single(plan.Accounts);
        Assert.True(account.EmailVerified);
        Assert.Empty(account.FederatedIdentities);
    }

    [Fact]
    public void LinksASocialRecordWithoutOwnEmailWhoseLegacySubjectResolvesTheEmail()
    {
        Auth0ExportRecord[] records = [new("facebook|654", null, false, "facebook")];
        var legacyEmailBySubject = new Dictionary<string, string> { ["facebook|654"] = "legacy@example.com" };

        var plan = LegacyAccountPlanner.Plan(records, legacyEmailBySubject, NoProfilesByEmail, NoProfilesByAuth0Subject);

        Assert.Equal([new FederatedIdentityLink("facebook", "654")], Assert.Single(plan.Accounts).FederatedIdentities);
    }

    [Fact]
    public void ResolvesTheEmailOfAnEmaillessRecordThroughTheLegacyMap()
    {
        Auth0ExportRecord[] records = [new("auth0|1", null, false, "Username-Password-Authentication")];
        var legacyEmailBySubject = new Dictionary<string, string> { ["auth0|1"] = "legacy@example.com" };

        var plan = LegacyAccountPlanner.Plan(records, legacyEmailBySubject, NoProfilesByEmail, NoProfilesByAuth0Subject);

        var account = Assert.Single(plan.Accounts);
        Assert.Equal("legacy@example.com", account.Email);
    }

    [Fact]
    public void KeepsAnEmaillessRecordWithAMigratedProfileAsAnAccountKeyedByTheAuth0Id()
    {
        Auth0ExportRecord[] records = [new("facebook|1234", null, false, "facebook")];
        var profileIdByAuth0Subject = new Dictionary<string, long> { ["facebook|1234"] = 42 };

        var plan = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, profileIdByAuth0Subject);

        var account = Assert.Single(plan.Accounts);
        Assert.Null(account.Email);
        Assert.Equal("facebook-1234", account.Username);
        Assert.Equal(42, account.ProfileId);
        Assert.Equal([new FederatedIdentityLink("facebook", "1234")], account.FederatedIdentities);
        Assert.Equal(0, plan.DroppedNoContactNoProfile);
    }

    [Fact]
    public void UsesTheAuth0PrefixAsTheUsernameAliasForAnAuth0Subject()
    {
        Auth0ExportRecord[] records = [new("auth0|abc", null, false, "Username-Password-Authentication")];
        var profileIdByAuth0Subject = new Dictionary<string, long> { ["auth0|abc"] = 7 };

        var plan = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, profileIdByAuth0Subject);

        Assert.Equal("auth0-abc", Assert.Single(plan.Accounts).Username);
        Assert.Empty(Assert.Single(plan.Accounts).FederatedIdentities);
    }

    [Fact]
    public void DropsAnEmaillessRecordWithoutAMigratedProfileAndCountsIt()
    {
        Auth0ExportRecord[] records = [new("facebook|1234", null, false, "facebook")];

        var plan = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, NoProfilesByAuth0Subject);

        Assert.Empty(plan.Accounts);
        Assert.Equal(1, plan.DroppedNoContactNoProfile);
    }

    [Fact]
    public void LinksAnAccountToAProfileFoundByEmail()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", false, "Username-Password-Authentication")];
        var profileIdByEmail = new Dictionary<string, long> { ["member@example.com"] = 11 };

        var plan = LegacyAccountPlanner.Plan(records, NoLegacyEmails, profileIdByEmail, NoProfilesByAuth0Subject);

        Assert.Equal(11, Assert.Single(plan.Accounts).ProfileId);
    }

    [Fact]
    public void LinksAnAccountToAProfileFoundByAuth0SubjectWhenNoProfileMatchesTheEmail()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", false, "Username-Password-Authentication")];
        var profileIdByAuth0Subject = new Dictionary<string, long> { ["auth0|1"] = 22 };

        var plan = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, profileIdByAuth0Subject);

        Assert.Equal(22, Assert.Single(plan.Accounts).ProfileId);
    }

    [Fact]
    public void LeavesProfileIdNullWhenNoProfileMatches()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", false, "Username-Password-Authentication")];

        var plan = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, NoProfilesByAuth0Subject);

        Assert.Null(Assert.Single(plan.Accounts).ProfileId);
    }

    [Fact]
    public void GivesEveryAccountTermsAndConditionsEvenWithoutAPasswordConnection()
    {
        Auth0ExportRecord[] records = [new("google-oauth2|1", "member@example.com", true, "google-oauth2")];

        var plan = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, NoProfilesByAuth0Subject);

        Assert.Equal(["TERMS_AND_CONDITIONS"], Assert.Single(plan.Accounts).RequiredActions);
    }

    [Fact]
    public void ProducesNoFederatedIdentityForAnAuth0OnlyAccount()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", false, "Username-Password-Authentication")];

        var plan = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, NoProfilesByAuth0Subject);

        Assert.Empty(Assert.Single(plan.Accounts).FederatedIdentities);
    }

    [Fact]
    public void AssignsADeterministicIdStableAcrossRepeatedPlanningRuns()
    {
        Auth0ExportRecord[] records = [new("auth0|1", "member@example.com", false, "Username-Password-Authentication")];

        var first = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, NoProfilesByAuth0Subject);
        var second = LegacyAccountPlanner.Plan(records, NoLegacyEmails, NoProfilesByEmail, NoProfilesByAuth0Subject);

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
}
