// Story: TASK-5.20.6
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Library.MediaLibraryTests;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Profiles;

/// <summary>
/// A member sets, replaces and removes the avatar and the header of the own profile from the media
/// library or from a fresh photo; both are profile links, at most one each, and show to everyone while
/// the profile is active.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class ProfileMediaTests(UploadsFixture fixture)
{
    private async Task<long> LibraryPhotoAsync(string token, byte[]? bytes = null)
    {
        using var response = await UploadToLibraryAsync(fixture.Client, token, bytes ?? Jpeg(500, 400));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    private async Task<long> ProfileIdAsync(string token)
    {
        using var me = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me", token);
        return (await BodyAsync(me)).GetProperty("profile").GetProperty("id").GetInt64();
    }

    private async Task<JsonElement> PublicProfileAsync(long profileId)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/profiles/{profileId}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task PlaceAsync(params long[] items)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var placement = scope.ServiceProvider.GetRequiredService<MediaPlacementService>();
        while (await placement.RunOnceAsync(CancellationToken.None, items))
        {
        }
    }

    private async Task<HttpResponseMessage> UploadPhotoAsync(string token, string route, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "me.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/me/{route}/photo") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    [Theory]
    [InlineData("avatar", "avatarUrl", MediaLinkRole.Avatar)]
    [InlineData("header", "profileImageUrl", MediaLinkRole.Header)]
    public async Task GivenAFileInTheLibrary_WhenTheMemberChoosesItAsAvatarOrHeader_ThenEveryoneSeesItOnTheProfile(
        string route, string property, MediaLinkRole role)
    {
        var token = NewMemberToken();
        var profileId = await ProfileIdAsync(token);
        var file = await LibraryPhotoAsync(token);

        using var put = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/{route}", token, new { mediaItemId = file });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var url = (await BodyAsync(put)).GetProperty(property).GetString();
        Assert.False(string.IsNullOrEmpty(url));
        Assert.StartsWith(fixture.PublicBaseUrl, url);
        Assert.Equal(url, (await PublicProfileAsync(profileId)).GetProperty(property).GetString());
        await using var db = fixture.CreateDbContext();
        var link = await db.MediaLinks.SingleAsync(l => l.ProfileId == profileId && l.Role == role);
        Assert.Equal(file, link.MediaItemId);
    }

    [Fact]
    public async Task GivenAnAvatar_WhenTheMemberCommentsOnAStory_ThenTheCommentAndTheStoryShowTheAvatar()
    {
        var author = NewMemberToken();
        var draft = await UploadDraftAsync(fixture.Client, author, Jpeg(300, 200));
        using var published = await PublishAsync(fixture.Client, author, draft.Id, new PublishStoryRequest("Avatar story", null, "cichlids"));
        var slug = (await BodyAsync(published)).GetProperty("slug").GetString();
        var file = await LibraryPhotoAsync(author);
        using var put = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/avatar", author, new { mediaItemId = file });
        var avatarUrl = (await BodyAsync(put)).GetProperty("avatarUrl").GetString();

        using var comment = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/stories/{slug}/comments", author, new { body = "Hello" });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);

        using var story = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/stories/{slug}", null);
        Assert.Equal(avatarUrl, (await BodyAsync(story)).GetProperty("author").GetProperty("avatarUrl").GetString());
        using var comments = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/stories/{slug}/comments", null);
        var first = (await BodyAsync(comments)).GetProperty("items")[0];
        Assert.Equal(avatarUrl, first.GetProperty("author").GetProperty("avatarUrl").GetString());
    }

    [Fact]
    public async Task GivenAnAvatar_WhenTheMemberChoosesAnotherFile_ThenItReplacesTheAvatarAndTheFirstFileStaysInTheLibrary()
    {
        var token = NewMemberToken();
        var profileId = await ProfileIdAsync(token);
        var first = await LibraryPhotoAsync(token);
        var second = await LibraryPhotoAsync(token, Jpeg(600, 600));
        using var one = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/avatar", token, new { mediaItemId = first });
        using var two = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/avatar", token, new { mediaItemId = second });

        Assert.Equal(HttpStatusCode.OK, two.StatusCode);
        await using var db = fixture.CreateDbContext();
        var links = await db.MediaLinks.Where(l => l.ProfileId == profileId && l.Role == MediaLinkRole.Avatar).ToListAsync();
        Assert.Equal(second, Assert.Single(links).MediaItemId);
        Assert.True(await db.MediaItems.AnyAsync(m => m.Id == first));
        var firstVariants = await db.MediaVariants.Where(v => v.MediaItemId == first).ToListAsync();
        Assert.All(firstVariants, v => Assert.StartsWith("private/", v.StorageKey));
    }

    [Fact]
    public async Task GivenAnAvatarAndAHeader_WhenTheMemberRemovesThem_ThenOnlyTheLinksGoAndTheFilesStay()
    {
        var token = NewMemberToken();
        var profileId = await ProfileIdAsync(token);
        var avatarFile = await LibraryPhotoAsync(token);
        var headerFile = await LibraryPhotoAsync(token);
        using var a = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/avatar", token, new { mediaItemId = avatarFile });
        using var h = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/header", token, new { mediaItemId = headerFile });

        using var removeAvatar = await SendAsync(fixture.Client, HttpMethod.Delete, "/api/me/avatar", token);
        Assert.Equal(HttpStatusCode.OK, removeAvatar.StatusCode);
        var afterAvatar = await BodyAsync(removeAvatar);
        Assert.Equal(JsonValueKind.Null, afterAvatar.GetProperty("avatarUrl").ValueKind);
        Assert.False(string.IsNullOrEmpty(afterAvatar.GetProperty("profileImageUrl").GetString()));

        using var removeHeader = await SendAsync(fixture.Client, HttpMethod.Delete, "/api/me/header", token);
        Assert.Equal(HttpStatusCode.OK, removeHeader.StatusCode);
        using var removeAgain = await SendAsync(fixture.Client, HttpMethod.Delete, "/api/me/header", token);
        Assert.Equal(HttpStatusCode.OK, removeAgain.StatusCode);

        var profile = await PublicProfileAsync(profileId);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("avatarUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("profileImageUrl").ValueKind);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaLinks.AnyAsync(l => l.ProfileId == profileId));
        Assert.Equal(2, await db.MediaItems.CountAsync(m => m.Id == avatarFile || m.Id == headerFile));
    }

    [Fact]
    public async Task GivenAFreshPhoto_WhenTheMemberUploadsItAsAvatar_ThenItIsTheAvatarAndLiesInTheLibrary()
    {
        var token = NewMemberToken();
        var profileId = await ProfileIdAsync(token);

        using var response = await UploadPhotoAsync(token, "avatar", Jpeg(640, 480));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answered = (await BodyAsync(response)).GetProperty("avatarUrl").GetString();
        Assert.StartsWith(fixture.PublicBaseUrl, answered);
        Assert.Equal(answered, (await PublicProfileAsync(profileId)).GetProperty("avatarUrl").GetString());
        using var library = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/media", token);
        var item = (await BodyAsync(library)).GetProperty("items")[0];
        Assert.Contains("profile_avatar", item.GetProperty("placeKinds").EnumerateArray().Select(k => k.GetString()));
    }

    [Fact]
    public async Task GivenAnUndecodablePhoto_WhenTheMemberUploadsItAsHeader_ThenItIsRefusedAndNothingChanges()
    {
        var token = NewMemberToken();
        var profileId = await ProfileIdAsync(token);

        using var response = await UploadPhotoAsync(token, "header", UndecodableJpeg());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "profile_photo_invalid");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaLinks.AnyAsync(l => l.ProfileId == profileId));
    }

    [Fact]
    public async Task GivenAFileOfAnotherMember_WhenAMemberTriesToSetItAsAvatar_ThenItAnswersLikeAMissingFileAndNothingChanges()
    {
        var owner = NewMemberToken();
        var stranger = NewMemberToken();
        var strangerId = await ProfileIdAsync(stranger);
        var file = await LibraryPhotoAsync(owner);

        using var put = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/avatar", stranger, new { mediaItemId = file });
        await AssertProblemAsync(put, HttpStatusCode.NotFound, "media_not_found");
        using var missing = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/header", stranger, new { mediaItemId = 9223372036854775000 });
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "media_not_found");

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaLinks.AnyAsync(l => l.ProfileId == strangerId));
        Assert.False(await db.MediaLinks.AnyAsync(l => l.MediaItemId == file));
    }

    [Fact]
    public async Task GivenNoTokenOrNoFileId_WhenSettingAnAvatar_ThenItAnswers401Or400()
    {
        using var anonymous = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/avatar", null, new { mediaItemId = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var anonymousRemove = await SendAsync(fixture.Client, HttpMethod.Delete, "/api/me/header", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRemove.StatusCode);

        using var empty = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/avatar", NewMemberToken(), new { });
        await AssertProblemAsync(empty, HttpStatusCode.BadRequest, "profile_media_invalid");
    }

    [Fact]
    public async Task GivenAVideo_WhenTheMemberSetsItAsAvatar_ThenItIsRefused()
    {
        var token = NewMemberToken();
        var profileId = await ProfileIdAsync(token);
        long video;
        await using (var db = fixture.CreateDbContext())
        {
            var item = new MediaItem { OwnerProfileId = profileId, Kind = MediaKind.Video, StorageKey = $"private/library/{profileId}/v.mp4", CreatedAt = DateTimeOffset.UtcNow };
            db.MediaItems.Add(item);
            await db.SaveChangesAsync();
            video = item.Id;
        }

        using var put = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/avatar", token, new { mediaItemId = video });

        await AssertProblemAsync(put, HttpStatusCode.BadRequest, "profile_media_not_photo");
    }

    [Fact]
    public async Task GivenAnAvatarOnAnActiveProfile_WhenTheProfileIsSuspended_ThenAnonymousViewersGetNoAvatar()
    {
        var token = NewMemberToken();
        var profileId = await ProfileIdAsync(token);
        var file = await LibraryPhotoAsync(token);
        using var put = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/avatar", token, new { mediaItemId = file });
        Assert.False(string.IsNullOrEmpty((await PublicProfileAsync(profileId)).GetProperty("avatarUrl").GetString()));
        string publicKey;
        await using (var db = fixture.CreateDbContext())
        {
            publicKey = (await db.MediaVariants.SingleAsync(v => v.MediaItemId == file && v.Label == "thumb")).StorageKey;
            Assert.False(publicKey.StartsWith("private/", StringComparison.Ordinal));
            await db.Profiles.Where(p => p.Id == profileId).ExecuteUpdateAsync(s => s.SetProperty(p => p.SuspendedUntil, DateTimeOffset.UtcNow.AddDays(1)));
        }

        await PlaceAsync(file);

        Assert.Equal(JsonValueKind.Null, (await PublicProfileAsync(profileId)).GetProperty("avatarUrl").ValueKind);
        Assert.False(await fixture.ObjectStore.ExistsAsync(publicKey));
    }

    [Fact]
    public async Task GivenAnAvatar_WhenTheOwnerDeletesTheFileFromTheLibrary_ThenTheAvatarIsGone()
    {
        var token = NewMemberToken();
        var profileId = await ProfileIdAsync(token);
        var file = await LibraryPhotoAsync(token);
        using var put = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/avatar", token, new { mediaItemId = file });

        using var delete = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{file}", token);

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await PublicProfileAsync(profileId)).GetProperty("avatarUrl").ValueKind);
    }
}
