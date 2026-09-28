using Cichlids.Etl.Identity;

namespace Cichlids.Etl.Tests;

public sealed class KeycloakUserIndexTests
{
    private static readonly Guid PlannedId = Guid.Parse("0f5a3c1e-2b4d-5e6f-8a9b-0c1d2e3f4a5b");

    private static readonly PlannedAccount EmaillessAccount = new(
        PlannedId, "facebook-1234", null, false, ["TERMS_AND_CONDITIONS"], [new FederatedIdentityLink("facebook", "1234")], 42);

    [Fact]
    public void MatchesAnEmaillessAccountToTheUserWithItsUsernameAndPlannedId()
    {
        var index = IndexByUsername("facebook-1234", new ExistingKeycloakUser(PlannedId.ToString(), false));

        var match = index.Match(EmaillessAccount);

        Assert.Equal(new AccountMatch(AccountMatchKind.Existing, PlannedId.ToString()), match);
    }

    [Fact]
    public void ReportsTheUsernameAsTakenWhenAnotherUserHoldsAnEmaillessAccountsUsername()
    {
        var index = IndexByUsername("facebook-1234", new ExistingKeycloakUser(Guid.NewGuid().ToString(), true));

        var match = index.Match(EmaillessAccount);

        Assert.Equal(new AccountMatch(AccountMatchKind.UsernameTaken, null), match);
    }

    [Fact]
    public void ReportsAnEmaillessAccountAsMissingWhenNoUserHasItsUsername()
    {
        var index = new KeycloakUserIndex(new Dictionary<string, ExistingKeycloakUser>(), new Dictionary<string, ExistingKeycloakUser>());

        var match = index.Match(EmaillessAccount);

        Assert.Equal(new AccountMatch(AccountMatchKind.Missing, null), match);
    }

    private static KeycloakUserIndex IndexByUsername(string username, ExistingKeycloakUser user) =>
        new(new Dictionary<string, ExistingKeycloakUser>(), new Dictionary<string, ExistingKeycloakUser> { [username] = user });
}
