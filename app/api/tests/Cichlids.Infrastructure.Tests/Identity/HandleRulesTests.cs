using Cichlids.Infrastructure.Identity;

namespace Cichlids.Infrastructure.Tests.Identity;

public sealed class HandleRulesTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("Anna.Maria_7", "anna.maria_7")]
    [InlineData("  ZEBRA ", "zebra")]
    public void Lowercases_and_trims_a_valid_handle(string input, string expected)
    {
        var result = HandleRules.Check(input);

        Assert.Equal(HandleCheck.Valid, result.Check);
        Assert.Equal(expected, result.Handle);
    }

    [Theory]
    [InlineData("a_b")]
    [InlineData("a1.b2")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123")]
    [InlineData("x.y")]
    public void Accepts_the_allowed_forms(string input)
    {
        Assert.Equal(HandleCheck.Valid, HandleRules.Check(input).Check);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxyz01234")]
    [InlineData("1abc")]
    [InlineData("_abc")]
    [InlineData(".abc")]
    [InlineData("abc.")]
    [InlineData("abc_")]
    [InlineData("a..b")]
    [InlineData("ab-cd")]
    [InlineData("ab cd")]
    [InlineData("ab@cd.de")]
    [InlineData("anna@example.com")]
    [InlineData("fischä")]
    [InlineData("١٢٣")]
    public void Refuses_malformed_handles(string? input)
    {
        Assert.Equal(HandleCheck.Invalid, HandleRules.Check(input).Check);
    }

    [Theory]
    [InlineData("user12345678")]
    [InlineData("USER00000001")]
    [InlineData("guest_1")]
    [InlineData("guest_4711")]
    [InlineData("provisional-0123abcd")]
    [InlineData("provisional-")]
    public void Recognises_the_forms_of_generated_names(string input)
    {
        Assert.Equal(HandleCheck.Generated, HandleRules.Check(input).Check);
    }

    [Theory]
    [InlineData("user1234567")]
    [InlineData("user123456789")]
    [InlineData("guest_")]
    [InlineData("guest")]
    [InlineData("username")]
    public void Leaves_near_misses_of_generated_names_to_the_other_rules(string input)
    {
        Assert.NotEqual(HandleCheck.Generated, HandleRules.Check(input).Check);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Administrator")]
    [InlineData("cichlids")]
    [InlineData("support")]
    [InlineData("help")]
    [InlineData("moderator")]
    [InlineData("system")]
    [InlineData("api")]
    [InlineData("www")]
    [InlineData("root")]
    [InlineData("official")]
    [InlineData("community-archive")]
    [InlineData("former-member-12")]
    [InlineData("forum-member-3")]
    public void Reserves_the_names_of_the_platform(string input)
    {
        Assert.Equal(HandleCheck.Reserved, HandleRules.Check(input).Check);
    }

    [Fact]
    public void Leaves_names_that_only_contain_a_reserved_word_free()
    {
        Assert.Equal(HandleCheck.Valid, HandleRules.Check("admin2").Check);
        Assert.Equal(HandleCheck.Valid, HandleRules.Check("my.help").Check);
    }
}
