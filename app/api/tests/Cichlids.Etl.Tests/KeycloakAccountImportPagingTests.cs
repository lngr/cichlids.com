// Story: task-3.15
using Cichlids.Etl.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Runs the Keycloak import scenario with a user page size of two and a creation chunk size of
/// three, so reading the realm spans several pages and creating the accounts spans several
/// partial imports, and checks that both runs see and create every account exactly once.
/// </summary>
public sealed class KeycloakAccountImportPagingTests(KeycloakAccountImportPagingTests.SmallPagesScenario scenario)
    : IClassFixture<KeycloakAccountImportPagingTests.SmallPagesScenario>
{
    [Fact]
    public async Task CreatesEveryAccountExactlyOnce()
    {
        foreach (var email in new[] { "alice@example.test", "bob@example.test", "carol@example.test", "dave@example.test", "grace@example.test", "ivy@example.test", "judy@example.test" })
        {
            var matches = await scenario.Admin.GetArrayAsync($"users?email={Uri.EscapeDataString(email)}&exact=true");
            Assert.True(matches.Count == 1, $"expected exactly one user with email {email}, found {matches.Count}");
        }

        Assert.Single(await scenario.Admin.GetArrayAsync("users?username=erin&exact=true"));

        var all = await scenario.Admin.GetArrayAsync("users?briefRepresentation=true&first=0&max=100");
        Assert.Equal(13, all.Count);
    }

    [Fact]
    public void FindsEveryCreatedUserOnTheSecondRun()
    {
        var first = scenario.FirstRun;
        Assert.Equal(6, first.Created);
        Assert.Equal(1, first.Existing);
        Assert.Equal(5, first.ProfileLinksInserted);
        Assert.Equal(0, first.Unresolved);

        var second = scenario.SecondRun;
        Assert.Equal(0, second.Created);
        Assert.Equal(7, second.Existing);
        Assert.Equal(2, second.AccountConflictsByReason["username_taken"]);
        Assert.Equal(1, second.AccountConflictsByReason["existing_unverified"]);
        Assert.Equal(0, second.ProfileLinksInserted);
        Assert.Equal(5, second.ProfileLinksPresent);
        Assert.Equal(0, second.Unresolved);
    }

    [Fact]
    public async Task LinksTheProfilesAndSocialIdentitiesOfAccountsFromEveryChunk()
    {
        var subjects = new Dictionary<string, string>
        {
            ["alice"] = await UserIdAsync("email=alice%40example.test"),
            ["carol"] = await UserIdAsync("email=carol%40example.test"),
            ["dave"] = await UserIdAsync("email=dave%40example.test"),
            ["erin"] = await UserIdAsync("username=erin"),
            ["grace"] = scenario.PreexistingGraceId,
        };

        await using var db = scenario.Fixture.CreateTargetContext();
        var oidc = await db.ProfileIdentities.Where(i => i.Provider == "oidc").ToListAsync();
        foreach (var (profile, subject) in subjects)
        {
            Assert.Contains(oidc, i => i.Subject == subject && i.ProfileId == scenario.ProfileIds[profile]);
        }

        Assert.Equal(oidc.Count, oidc.Select(i => i.Subject).Distinct().Count());

        Assert.Equal(["google"], await FederatedAliasesAsync(subjects["carol"]));
        Assert.Equal(["facebook"], await FederatedAliasesAsync(subjects["dave"]));
        Assert.Equal(["facebook"], await FederatedAliasesAsync(subjects["erin"]));
    }

    private async Task<string> UserIdAsync(string query)
    {
        var matches = await scenario.Admin.GetArrayAsync($"users?{query}&exact=true");
        return Assert.Single(matches).GetProperty("id").GetString()!;
    }

    private async Task<string[]> FederatedAliasesAsync(string userId)
    {
        var links = await scenario.Admin.GetArrayAsync($"users/{userId}/federated-identity");
        return links.Select(l => l.GetProperty("identityProvider").GetString()!).ToArray();
    }

    /// <summary>
    /// The Keycloak import scenario with a user page size of two and a creation chunk size of
    /// three.
    /// </summary>
    public sealed class SmallPagesScenario() : KeycloakAccountImportTests.Scenario(userPageSize: 2, importChunkSize: 3);
}
