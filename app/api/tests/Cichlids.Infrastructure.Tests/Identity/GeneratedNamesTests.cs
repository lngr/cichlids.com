using System.Security.Cryptography;
using System.Text;
using Cichlids.Infrastructure.Identity;

namespace Cichlids.Infrastructure.Tests.Identity;

public sealed class GeneratedNamesTests
{
    [Theory]
    [InlineData("handle:1")]
    [InlineData("login:alice@example.test")]
    [InlineData("")]
    public void Generate_returns_user_followed_by_eight_digits_without_a_leading_zero(string input)
    {
        var name = new GeneratedNames("secret-a").Generate(input);

        Assert.Matches("^user[1-9][0-9]{7}$", name);
    }

    [Fact]
    public void Generate_is_deterministic_for_the_same_input_and_secret()
    {
        Assert.Equal(new GeneratedNames("secret-a").Generate("handle:42"), new GeneratedNames("secret-a").Generate("handle:42"));
    }

    [Fact]
    public void Generate_differs_between_secrets_and_between_prefixes()
    {
        var names = new GeneratedNames("secret-a");

        Assert.NotEqual(names.Generate("handle:42"), new GeneratedNames("secret-b").Generate("handle:42"));
        Assert.NotEqual(names.Generate(GeneratedNames.HandleInput("42")), names.Generate(GeneratedNames.LoginInput("42")));
    }

    [Fact]
    public void Inputs_carry_their_domain_prefix()
    {
        Assert.Equal("handle:42", GeneratedNames.HandleInput("42"));
        Assert.Equal("login:google-oauth2|1", GeneratedNames.LoginInput("google-oauth2|1"));
        Assert.Equal("guest:a@example.test", GeneratedNames.GuestInput("a@example.test"));
    }

    [Fact]
    public void Hash_is_the_hmac_sha256_of_the_input_keyed_with_the_secret()
    {
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret-a"), Encoding.UTF8.GetBytes("guest:a@example.test"));

        Assert.Equal(expected, new GeneratedNames("secret-a").Hash("guest:a@example.test"));
    }

    [Fact]
    public void GenerateUnique_returns_the_first_candidate_when_it_is_not_taken()
    {
        var names = new GeneratedNames("secret-a");

        Assert.Equal(names.Generate("handle:42"), names.GenerateUnique("handle:42", _ => false));
    }

    [Fact]
    public void GenerateUnique_derives_further_candidates_from_a_counter_on_the_hmac_input()
    {
        var names = new GeneratedNames("secret-a");
        var first = names.Generate("handle:42");
        var second = names.Generate("handle:42:1");
        var third = names.Generate("handle:42:2");

        var attempts = new List<string>();
        var name = names.GenerateUnique("handle:42", candidate =>
        {
            attempts.Add(candidate);
            return candidate == first || candidate == second;
        });

        Assert.Equal(third, name);
        Assert.Equal([first, second, third], attempts);
        Assert.Equal([first, second, third], names.Candidates("handle:42").Take(3));
    }

    [Fact]
    public void Rejects_an_empty_secret()
    {
        Assert.Throws<ArgumentException>(() => new GeneratedNames(string.Empty));
    }
}
