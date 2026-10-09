using Cichlids.Infrastructure.Slugs;

namespace Cichlids.Infrastructure.Tests;

public sealed class SlugGeneratorTests
{
    [Fact]
    public void Generate_is_deterministic_for_the_same_input_and_secret()
    {
        var generator = new SlugGenerator("secret-a");

        var first = generator.Generate("post:42");
        var second = generator.Generate("post:42");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Generate_differs_between_secrets_for_the_same_input()
    {
        var withSecretA = new SlugGenerator("secret-a").Generate("post:42");
        var withSecretB = new SlugGenerator("secret-b").Generate("post:42");

        Assert.NotEqual(withSecretA, withSecretB);
    }

    [Theory]
    [InlineData("post:1")]
    [InlineData("post:42")]
    [InlineData("post:999999")]
    [InlineData("")]
    public void Generate_returns_ten_lowercase_base36_characters(string input)
    {
        var slug = new SlugGenerator("secret-a").Generate(input);

        Assert.Equal(10, slug.Length);
        Assert.Matches("^[0-9a-z]{10}$", slug);
    }

    [Fact]
    public void GenerateUnique_returns_the_first_candidate_when_it_is_not_taken()
    {
        var generator = new SlugGenerator("secret-a");

        var slug = generator.GenerateUnique("post:42", _ => false);

        Assert.Equal(generator.Generate("post:42"), slug);
    }

    [Fact]
    public void GenerateUnique_retries_with_a_counter_suffix_on_the_hmac_input_until_a_free_candidate_is_found()
    {
        var generator = new SlugGenerator("secret-a");
        var firstCandidate = generator.Generate("post:42");
        var secondCandidate = generator.Generate("post:42:1");

        var attempts = new List<string>();
        var slug = generator.GenerateUnique("post:42", candidate =>
        {
            attempts.Add(candidate);
            return candidate == firstCandidate;
        });

        Assert.Equal(secondCandidate, slug);
        Assert.Equal([firstCandidate, secondCandidate], attempts);
    }
}
