using System.Net;
using System.Net.Http.Headers;
using Cichlids.Api.Tests.Features.Uploads;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Media;

/// <summary>
/// Photos of a tank for followers and selected members live in the private area and are served through
/// signed links; a link issued to a follower or selected member stops working when the follow is removed
/// or the member is no longer selected, and anonymous viewers never get a link.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class TankAudienceMediaTests(UploadsFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient client = null!;

    public Task InitializeAsync()
    {
        _host = fixture.CreateHost(new Dictionary<string, string?> { ["MediaPlacement:ImmediateBudget"] = "00:00:00" }, null);
        client = _host.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await _host.DisposeAsync();
    }

    private sealed record Viewer(string Token, string Handle);

    private static Viewer NewViewer()
    {
        var handle = $"aud-{Guid.NewGuid():N}"[..24];
        return new Viewer(TestTokens.Create(Guid.NewGuid().ToString(), handle), handle);
    }

    private async Task<long> ProfileIdAsync(Viewer viewer)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Profiles.Where(p => p.Username == viewer.Handle).Select(p => p.Id).SingleAsync();
    }

    private async Task<(string OwnerToken, long Tank, long Item)> TankWithCarePhotoAsync(string profile, string stream)
    {
        var (token, tank) = await OwnTankAsync(client);
        var entry = await LogAsync(client, token, tank);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg(800, 600));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/care/{entry}/photos") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var visibility = await SendAsync(client, HttpMethod.Put, $"/api/tanks/{tank}/visibility", token, new { profile, stream });
        Assert.Equal(HttpStatusCode.OK, visibility.StatusCode);
        await using var db = fixture.CreateDbContext();
        var item = await db.MediaLinks.Where(l => l.CareEntryId == entry).Select(l => l.MediaItemId).SingleAsync();
        return (token, tank, item);
    }

    private async Task<string> CareThumbAsync(string token, long tank)
    {
        using var list = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        return (await BodyAsync(list)).GetProperty("items")[0].GetProperty("photos")[0].GetProperty("urls").GetProperty("thumb").GetString()!;
    }

    private async Task AssertServedAsync(string url, HttpStatusCode expected)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(expected, response.StatusCode);
    }

    private async Task<List<string>> KeysAsync(long item)
    {
        await using var db = fixture.CreateDbContext();
        return await db.MediaVariants.Where(v => v.MediaItemId == item).Select(v => v.StorageKey).ToListAsync();
    }

    [Fact]
    public async Task GivenATankForFollowers_ThenItsPhotosLiveInThePrivateAreaAndAnApprovedFollowerGetsASignedLink()
    {
        var (owner, tank, item) = await TankWithCarePhotoAsync("followers", "followers");
        var follower = NewViewer();
        using var ask = await SendAsync(client, HttpMethod.Put, $"/api/tanks/{tank}/follow", follower.Token);
        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);
        var followerId = await ProfileIdAsync(follower);

        using (var scope = _host.Services.CreateScope())
        {
            while (await scope.ServiceProvider.GetRequiredService<Cichlids.Api.Features.Media.MediaPlacementService>().RunOnceAsync(CancellationToken.None, [item])) { }
        }

        Assert.All(await KeysAsync(item), key => Assert.StartsWith("private/variants/", key));
        using (var pending = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/care", follower.Token))
        {
            await AssertProblemAsync(pending, HttpStatusCode.NotFound, "care_not_found");
        }

        (await SendAsync(client, HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{followerId}/approve", owner)).Dispose();
        var link = await CareThumbAsync(follower.Token, tank);
        Assert.Contains("/api/media/photos/", link);
        await AssertServedAsync(link, HttpStatusCode.OK);

        using var anonymous = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/care", null);
        await AssertProblemAsync(anonymous, HttpStatusCode.NotFound, "care_not_found");
    }

    [Fact]
    public async Task GivenALinkIssuedToAFollower_WhenTheOwnerRemovesTheFollower_ThenTheSameLinkIsRefused()
    {
        var (owner, tank, _) = await TankWithCarePhotoAsync("followers", "followers");
        var follower = NewViewer();
        (await SendAsync(client, HttpMethod.Put, $"/api/tanks/{tank}/follow", follower.Token)).Dispose();
        var followerId = await ProfileIdAsync(follower);
        (await SendAsync(client, HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{followerId}/approve", owner)).Dispose();
        var link = await CareThumbAsync(follower.Token, tank);
        await AssertServedAsync(link, HttpStatusCode.OK);

        using var remove = await SendAsync(client, HttpMethod.Delete, $"/api/tanks/{tank}/followers/{followerId}", owner);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        await AssertServedAsync(link, HttpStatusCode.NotFound);
        using var care = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/care", follower.Token);
        await AssertProblemAsync(care, HttpStatusCode.NotFound, "care_not_found");
    }

    [Fact]
    public async Task GivenALinkIssuedToAFollower_WhenTheFollowerStopsFollowing_ThenTheSameLinkIsRefused()
    {
        var (owner, tank, _) = await TankWithCarePhotoAsync("followers", "followers");
        var follower = NewViewer();
        (await SendAsync(client, HttpMethod.Put, $"/api/tanks/{tank}/follow", follower.Token)).Dispose();
        (await SendAsync(client, HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{await ProfileIdAsync(follower)}/approve", owner)).Dispose();
        var link = await CareThumbAsync(follower.Token, tank);
        await AssertServedAsync(link, HttpStatusCode.OK);

        (await SendAsync(client, HttpMethod.Delete, $"/api/tanks/{tank}/follow", follower.Token)).Dispose();

        await AssertServedAsync(link, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenALinkIssuedToASelectedMember_WhenTheOwnerRemovesTheMember_ThenTheSameLinkIsRefused()
    {
        var (owner, tank, _) = await TankWithCarePhotoAsync("selected", "selected");
        var member = NewViewer();
        using (var createsTheProfile = await SendAsync(client, HttpMethod.Delete, $"/api/tanks/{tank}/follow", member.Token)) { }
        var memberId = await ProfileIdAsync(member);
        using var add = await SendAsync(client, HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner, new { handle = member.Handle });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var link = await CareThumbAsync(member.Token, tank);
        await AssertServedAsync(link, HttpStatusCode.OK);

        using var remove = await SendAsync(client, HttpMethod.Delete, $"/api/tanks/{tank}/selected-members/{memberId}", owner);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        await AssertServedAsync(link, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GivenALinkIssuedToAFollower_WhenTheOwnerNarrowsTheTankToSelectedMembers_ThenTheSameLinkIsRefused()
    {
        var (owner, tank, _) = await TankWithCarePhotoAsync("followers", "followers");
        var follower = NewViewer();
        (await SendAsync(client, HttpMethod.Put, $"/api/tanks/{tank}/follow", follower.Token)).Dispose();
        (await SendAsync(client, HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{await ProfileIdAsync(follower)}/approve", owner)).Dispose();
        var link = await CareThumbAsync(follower.Token, tank);
        await AssertServedAsync(link, HttpStatusCode.OK);

        using var narrow = await SendAsync(client, HttpMethod.Put, $"/api/tanks/{tank}/visibility", owner, new { profile = "selected", stream = "selected" });
        Assert.Equal(HttpStatusCode.OK, narrow.StatusCode);

        await AssertServedAsync(link, HttpStatusCode.NotFound);
    }
}
