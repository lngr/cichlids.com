using Cichlids.Api.Features.Common;

namespace Cichlids.Api.Tests.Features.Common;

public class AuthorRowTests
{
    private const string BaseUrl = "https://media.test";
    private const string External = "https://example.test/avatars/carol.png";

    [Fact]
    public void ToDto_resolves_the_avatar_to_its_thumbnail()
    {
        var row = new AuthorRow(1, "carol", null, "variants/thumb/originals/a.jpg.jpg", "variants/copy/originals/a.jpg", External);

        Assert.Equal($"{BaseUrl}/variants/thumb/originals/a.jpg.jpg", row.ToDto(new UrlOnlyObjectStore(BaseUrl)).AvatarUrl);
    }

    [Fact]
    public void ToDto_resolves_an_avatar_with_only_a_public_copy_to_the_copy()
    {
        var row = new AuthorRow(1, "carol", null, null, "variants/copy/originals/a.jpg", External);

        Assert.Equal($"{BaseUrl}/variants/copy/originals/a.jpg", row.ToDto(new UrlOnlyObjectStore(BaseUrl)).AvatarUrl);
    }

    [Fact]
    public void ToDto_falls_back_to_the_external_avatar()
    {
        var row = new AuthorRow(1, "carol", null, null, null, External);

        Assert.Equal(External, row.ToDto(new UrlOnlyObjectStore(BaseUrl)).AvatarUrl);
    }

    [Fact]
    public void ToDto_refuses_an_avatar_key_outside_the_public_prefixes()
    {
        var row = new AuthorRow(1, "carol", null, "originals/a.jpg", null, External);

        Assert.Throws<InvalidOperationException>(() => row.ToDto(new UrlOnlyObjectStore(BaseUrl)));
    }
}
