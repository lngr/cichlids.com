using Cichlids.Infrastructure.Identity;

namespace Cichlids.Infrastructure.Tests.Identity;

public sealed class EmailLikeTests
{
    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("Alice.Smith+fish@mail.example.co.uk")]
    [InlineData(" bob@example.org ")]
    [InlineData("Bob (bob@example.org)")]
    [InlineData("contact: bob@example.org, thanks")]
    [InlineData("a@b.c")]
    public void Detects_a_value_containing_an_email_address(string value)
    {
        Assert.True(EmailLike.Contains(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("alice")]
    [InlineData("DM@Montreal")]
    [InlineData("Bigblue@6")]
    [InlineData("FiReR@T")]
    [InlineData("@home")]
    [InlineData("fish@")]
    [InlineData("a@b.")]
    [InlineData("a @ b.com")]
    [InlineData("dot.name")]
    public void Treats_a_value_without_an_address_shaped_part_as_not_email_like(string? value)
    {
        Assert.False(EmailLike.Contains(value));
    }
}
