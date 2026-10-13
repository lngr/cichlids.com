using Cichlids.Infrastructure.Identity;

namespace Cichlids.Infrastructure.Tests.Identity;

public sealed class ReservedUsernamesTests
{
    [Theory]
    [InlineData("community-archive")]
    [InlineData("Community-Archive")]
    [InlineData(" community-archive ")]
    [InlineData("former-member-1")]
    [InlineData("former-member-12345")]
    [InlineData("forum-member-7")]
    [InlineData("FORUM-MEMBER-904")]
    public void Reserves_the_system_profile_names(string value)
    {
        Assert.True(ReservedUsernames.IsReserved(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("alice")]
    [InlineData("community-archive-2")]
    [InlineData("my-community-archive")]
    [InlineData("former-member-")]
    [InlineData("former-member-x")]
    [InlineData("former-member-1a")]
    [InlineData("forum-member")]
    public void Leaves_other_names_free(string? value)
    {
        Assert.False(ReservedUsernames.IsReserved(value));
    }

    [Fact]
    public void Builds_the_placeholder_names_it_reserves()
    {
        Assert.True(ReservedUsernames.IsReserved(ReservedUsernames.CommunityArchive));
        Assert.True(ReservedUsernames.IsReserved(ReservedUsernames.FormerMember(42)));
        Assert.True(ReservedUsernames.IsReserved(ReservedUsernames.ForumMember(42)));
        Assert.Equal("former-member-42", ReservedUsernames.FormerMember(42));
        Assert.Equal("forum-member-42", ReservedUsernames.ForumMember(42));
    }
}
