using Cichlids.Etl.Identity;

namespace Cichlids.Etl.Tests;

public sealed class ExternalAvatarUrlTests
{
    [Theory]
    [InlineData("https://s.gravatar.com/avatar/deadbeef?s=480&r=pg&d=https%3A%2F%2Fcdn.auth0.com%2Favatars%2Fal.png")]
    [InlineData("https://secure.gravatar.com/avatar/deadbeef")]
    [InlineData("http://gravatar.com/avatar/deadbeef")]
    [InlineData("HTTPS://S.GRAVATAR.COM/avatar/deadbeef")]
    public void Recognises_every_gravatar_host_over_http_or_https(string url)
    {
        Assert.True(ExternalAvatarUrl.IsGravatar(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://lh3.googleusercontent.com/a/abc123=s96-c")]
    [InlineData("https://platform-lookaside.fbsbx.com/platform/profilepic/?asid=1&hash=abc")]
    [InlineData("https://scontent.xx.fbcdn.net/v/t1.0-1/pic.jpg")]
    [InlineData("https://notgravatar.com.example/avatar/deadbeef")]
    [InlineData("not-a-url")]
    public void Treats_every_other_avatar_source_as_not_gravatar(string? url)
    {
        Assert.False(ExternalAvatarUrl.IsGravatar(url));
    }
}
