using Cichlids.Etl.Identity;

namespace Cichlids.Etl.Tests;

public sealed class KeycloakUserIndexTests
{
    private static readonly Guid PlannedId = Guid.Parse("0f5a3c1e-2b4d-5e6f-8a9b-0c1d2e3f4a5b");

    private static readonly PlannedAccount EmaillessAccount = new(
        PlannedId, "erin", "user12345678", LoginUsernameSource.Legacy, null, false,
        ["TERMS_AND_CONDITIONS"], [new FederatedIdentityLink("facebook", "1234")], 42);

    private static readonly PlannedAccount EmailAccount = new(
        PlannedId, "Alice", "user87654321", LoginUsernameSource.Legacy, "alice@example.com", true,
        ["TERMS_AND_CONDITIONS"], [], 42);

    [Fact]
    public void MatchesAnEmaillessAccountToTheUserWithItsPlannedId()
    {
        var index = Index(byId: new() { [PlannedId.ToString()] = new ExistingKeycloakUser(PlannedId.ToString(), false) });

        Assert.Equal(new AccountMatch(AccountMatchKind.Existing, PlannedId.ToString()), index.Match(EmaillessAccount));
    }

    [Fact]
    public void ReportsTheUsernameAsTakenWhenAnotherUserHoldsAnEmaillessAccountsUsername()
    {
        var index = Index(byUsername: new() { ["erin"] = new ExistingKeycloakUser(Guid.NewGuid().ToString(), true) });

        Assert.Equal(new AccountMatch(AccountMatchKind.UsernameTaken, null), index.Match(EmaillessAccount));
    }

    [Fact]
    public void ReportsAnEmaillessAccountAsMissingWithItsUsernameWhenNoUserHasIt()
    {
        Assert.Equal(new AccountMatch(AccountMatchKind.Missing, null, "erin"), Index().Match(EmaillessAccount));
    }

    [Fact]
    public void ReportsAnEmailAccountAsMissingWithItsUsernameWhenNoUserHasItOrTheEmail()
    {
        Assert.Equal(new AccountMatch(AccountMatchKind.Missing, null, "Alice"), Index().Match(EmailAccount));
    }

    [Fact]
    public void GivesAnEmailAccountItsGeneratedUsernameWhenAnotherUserHoldsItsLegacyName()
    {
        var index = Index(byUsername: new() { ["alice"] = new ExistingKeycloakUser(Guid.NewGuid().ToString(), true) });

        Assert.Equal(new AccountMatch(AccountMatchKind.Missing, null, "user87654321"), index.Match(EmailAccount));
    }

    [Fact]
    public void ReportsTheUsernameAsTakenWhenOtherUsersHoldBothNamesOfAnEmailAccount()
    {
        var index = Index(byUsername: new()
        {
            ["alice"] = new ExistingKeycloakUser(Guid.NewGuid().ToString(), true),
            ["user87654321"] = new ExistingKeycloakUser(Guid.NewGuid().ToString(), true),
        });

        Assert.Equal(new AccountMatch(AccountMatchKind.UsernameTaken, null), index.Match(EmailAccount));
    }

    [Fact]
    public void ReportsTheUsernameAsTakenWhenAnotherUserHoldsTheEmailOfAnEmailAccountAsUsername()
    {
        var index = Index(byUsername: new() { ["alice@example.com"] = new ExistingKeycloakUser(Guid.NewGuid().ToString(), true) });

        Assert.Equal(new AccountMatch(AccountMatchKind.UsernameTaken, null), index.Match(EmailAccount));
    }

    [Fact]
    public void MatchesAnEmailAccountToTheUserWithItsVerifiedEmail()
    {
        var existingId = Guid.NewGuid().ToString();
        var index = Index(byEmail: new() { ["alice@example.com"] = new ExistingKeycloakUser(existingId, true) });

        Assert.Equal(new AccountMatch(AccountMatchKind.Existing, existingId), index.Match(EmailAccount));
    }

    private static KeycloakUserIndex Index(
        Dictionary<string, ExistingKeycloakUser>? byEmail = null,
        Dictionary<string, ExistingKeycloakUser>? byUsername = null,
        Dictionary<string, ExistingKeycloakUser>? byId = null) =>
        new(byEmail ?? [], byUsername ?? [], byId ?? []);
}
