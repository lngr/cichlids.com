using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Common;

namespace Cichlids.Api.Tests.Features.Pictures;

[Collection(ApiCollection.Name)]
public class PictureCommentsEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task List_ReturnsOnlyVisibleCommentsOldestFirstWithAuthorOrPosterName()
    {
        var response = await fixture.Client.GetAsync($"/api/pictures/{fixture.Seed.PublishedPictureACanonicalSlug}/comments");
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<PagedResponse<CommentDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(2, body.Total);

        var withAuthor = body.Items[0];
        Assert.Equal("Great fish!", withAuthor.Body);
        Assert.NotNull(withAuthor.Author);
        Assert.Equal("bob", withAuthor.Author!.Username);
        Assert.Null(withAuthor.PosterName);

        var anonymous = body.Items[1];
        Assert.Equal("Nice!", anonymous.Body);
        Assert.Null(anonymous.Author);
        Assert.Equal("Guest123", anonymous.PosterName);

        Assert.DoesNotContain(body.Items, c => c.Body == "oops");
    }

    [Fact]
    public async Task List_UnknownSlugReturnsNotFound()
    {
        var response = await fixture.Client.GetAsync("/api/pictures/does-not-exist/comments");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_NonPublicPostReturnsNotFound()
    {
        var response = await fixture.Client.GetAsync($"/api/pictures/{fixture.Seed.DraftPictureSlug}/comments");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
