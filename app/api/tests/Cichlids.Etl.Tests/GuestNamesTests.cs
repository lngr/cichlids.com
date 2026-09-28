using System.Security.Cryptography;
using System.Text;
using Cichlids.Etl.Identity;
using Cichlids.Infrastructure.Identity;

namespace Cichlids.Etl.Tests;

public sealed class GuestNamesTests
{
    private const string Secret = "guest-names-test-secret"; // gitleaks:allow

    private static readonly GeneratedNames Names = new(Secret);

    [Fact]
    public void Numbers_distinct_addresses_from_one_in_the_order_of_their_keyed_hash()
    {
        var guestNames = GuestNames.Create(["b.guest@example.org", "a.guest@example.com", "c.guest@example.net"], Names);

        var expectedOrder = new[] { "a.guest@example.com", "b.guest@example.org", "c.guest@example.net" }
            .OrderBy(KeyedHashHex, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(3, guestNames.Count);
        Assert.Equal("guest_00001", guestNames.Resolve(expectedOrder[0]));
        Assert.Equal("guest_00002", guestNames.Resolve(expectedOrder[1]));
        Assert.Equal("guest_00003", guestNames.Resolve(expectedOrder[2]));
    }

    [Fact]
    public void Maps_the_same_address_from_different_sources_to_the_same_name_regardless_of_case_and_spacing()
    {
        var commentPosters = new[] { "Shared.Guest@Example.com", "other@example.org" };
        var forumAuthors = new[] { "  shared.guest@example.com " };

        var guestNames = GuestNames.Create(commentPosters.Concat(forumAuthors), Names);

        Assert.Equal(2, guestNames.Count);
        Assert.Equal(guestNames.Resolve("Shared.Guest@Example.com"), guestNames.Resolve("shared.guest@example.com"));
        Assert.Equal(guestNames.Resolve("Shared.Guest@Example.com"), guestNames.Resolve(" SHARED.GUEST@EXAMPLE.COM "));
        Assert.NotEqual(guestNames.Resolve("Shared.Guest@Example.com"), guestNames.Resolve("other@example.org"));
    }

    [Fact]
    public void Treats_a_name_that_contains_an_address_as_a_name_of_its_own()
    {
        var guestNames = GuestNames.Create(["Peter (peter@example.com)", "peter@example.com"], Names);

        Assert.Equal(2, guestNames.Count);
        Assert.Matches("^guest_[0-9]{5}$", guestNames.Resolve("Peter (peter@example.com)"));
        Assert.NotEqual(guestNames.Resolve("Peter (peter@example.com)"), guestNames.Resolve("peter@example.com"));
    }

    [Fact]
    public void Assigns_the_same_names_for_the_same_source_names_in_any_order()
    {
        var sourceNames = Enumerable.Range(1, 50).Select(i => $"guest{i}@example.com").ToList();

        var first = GuestNames.Create(sourceNames, Names);
        var second = GuestNames.Create(Enumerable.Reverse(sourceNames).Concat(sourceNames), new GeneratedNames(Secret));

        Assert.Equal(50, second.Count);
        Assert.All(sourceNames, name => Assert.Equal(first.Resolve(name), second.Resolve(name)));
        Assert.Equal(50, sourceNames.Select(first.Resolve).Distinct().Count());
    }

    [Fact]
    public void Orders_by_a_hash_keyed_with_the_secret()
    {
        var sourceNames = Enumerable.Range(1, 20).Select(i => $"guest{i}@example.com").ToList();

        var underSecretA = GuestNames.Create(sourceNames, new GeneratedNames("secret-a"));
        var underSecretB = GuestNames.Create(sourceNames, new GeneratedNames("secret-b"));

        Assert.NotEqual(sourceNames.Select(underSecretA.Resolve), sourceNames.Select(underSecretB.Resolve));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Guest Visitor")]
    [InlineData("DM@Montreal")]
    [InlineData("guest_00001")]
    public void Leaves_a_name_without_an_address_unchanged(string? posterName)
    {
        var guestNames = GuestNames.Create(["a.guest@example.com", posterName], Names);

        Assert.Equal(1, guestNames.Count);
        Assert.Equal(posterName, guestNames.Resolve(posterName));
    }

    [Fact]
    public void Widens_the_number_beyond_five_digits_for_more_than_99999_addresses()
    {
        var sourceNames = Enumerable.Range(1, 100_000).Select(i => $"g{i}@example.com").ToList();

        var guestNames = GuestNames.Create(sourceNames, Names);

        Assert.Equal(100_000, guestNames.Count);
        var assigned = sourceNames.Select(guestNames.Resolve).ToHashSet();
        Assert.Equal(100_000, assigned.Count);
        Assert.All(assigned, name => Assert.Matches("^guest_[0-9]{6}$", name));
        Assert.Contains("guest_000001", assigned);
        Assert.Contains("guest_100000", assigned);
    }

    [Fact]
    public void Refuses_to_resolve_an_address_that_was_not_collected()
    {
        var guestNames = GuestNames.Create(["a.guest@example.com"], Names);

        var exception = Assert.Throws<InvalidOperationException>(() => guestNames.Resolve("unknown@example.com"));
        Assert.DoesNotContain("unknown@example.com", exception.Message);
    }

    private static string KeyedHashHex(string key) => Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"guest:{key}")));
}
