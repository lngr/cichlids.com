using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Common;

namespace Cichlids.Api.Tests.Features.Tanks;

[Collection(ApiCollection.Name)]
public class TankCommentsEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task List_ReturnsOnlyVisibleComments()
    {
        var response = await fixture.Client.GetAsync($"/api/tanks/{fixture.Seed.PublishedTank.Id}/comments");
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<PagedResponse<CommentDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(1, body.Total);
        Assert.Equal("Beautiful setup!", body.Items[0].Body);
        Assert.DoesNotContain(body.Items, c => c.Body == "spam");
    }

    [Fact]
    public async Task List_UnknownTankReturnsNotFound()
    {
        var response = await fixture.Client.GetAsync("/api/tanks/999999999/comments");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
