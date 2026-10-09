using Cichlids.Infrastructure.Identity;

namespace Cichlids.Infrastructure.Tests.Identity;

public sealed class PublicHandleTests
{
    [Theory]
    [InlineData("alice")]
    [InlineData("DM@Montreal")]
    [InlineData("Aaron Giles")]
    public void Accepts_a_non_blank_value_that_is_not_email_like(string value)
    {
        Assert.True(PublicHandle.IsUsable(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("alice@example.com")]
    public void Rejects_a_blank_or_email_like_value(string? value)
    {
        Assert.False(PublicHandle.IsUsable(value));
    }
}
