using Cichlids.Etl.Identity;

namespace Cichlids.Etl.Tests;

public sealed class KeycloakUsernameRuleTests
{
    public static TheoryData<string, bool> LegacyUsernames()
    {
        var data = new TheoryData<string, bool>();
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "keycloak-usernames.tsv")))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('\t');
            data.Add(line[(separator + 1)..], line[..separator] == "accepted");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LegacyUsernames))]
    public void Agrees_with_Keycloak_on_legacy_usernames(string username, bool accepted)
    {
        Assert.Equal(accepted, KeycloakUsernameRule.IsAccepted(username));
    }

    // Each value was sent to the admin API of a Keycloak 26.6.4 realm; the expectation is its
    // answer.
    [Theory]
    [InlineData("abc\u00a0def", false)]
    [InlineData("tab\tname", false)]
    [InlineData("line\u2028sep", false)]
    [InlineData("abc\u0085x", false)]
    [InlineData("\u03a9mega", false)]
    [InlineData("\u0416enya", false)]
    [InlineData("e\u0301cole", false)]
    [InlineData("\u65e5\u672c\u8a9e", false)]
    [InlineData("\uff46\uff55\uff4c\uff4c", true)]
    [InlineData("fish\ud83d\udc1f", true)]
    [InlineData("ab\u00a7cd", false)]
    [InlineData("\u00c6\u00d8\u00c5", true)]
    [InlineData("ab", false)]
    [InlineData("abc", true)]
    [InlineData("ALICE", true)]
    [InlineData("\u0130stanbul", false)]
    [InlineData("a\u200bb", true)]
    [InlineData("x\u180ey", false)]
    [InlineData("a\u00b7b", true)]
    [InlineData("a\u02bcb", true)]
    [InlineData("ab\u2019c", true)]
    [InlineData(" abc", false)]
    [InlineData("abc ", false)]
    public void Agrees_with_Keycloak_on_characters_outside_the_legacy_data(string username, bool accepted)
    {
        Assert.Equal(accepted, KeycloakUsernameRule.IsAccepted(username));
    }

    [Fact]
    public void Accepts_at_most_255_characters()
    {
        Assert.True(KeycloakUsernameRule.IsAccepted(new string('x', 255)));
        Assert.False(KeycloakUsernameRule.IsAccepted(new string('x', 256)));
    }

    [Theory]
    [InlineData("ALICE", "alice")]
    [InlineData("\u00c6\u00d8\u00c5", "\u00e6\u00f8\u00e5")]
    [InlineData("\u0130x", "i\u0307x")]
    public void Normalizes_to_the_lowercase_form_Keycloak_stores(string username, string normalized)
    {
        Assert.Equal(normalized, KeycloakUsernameRule.Normalize(username));
    }
}
