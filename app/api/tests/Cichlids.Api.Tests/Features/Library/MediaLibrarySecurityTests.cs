// Story: TASK-5.20.4
using System.Net;
using System.Net.Http.Headers;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Library.MediaLibraryTests;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Library;

/// <summary>
/// Other members and anonymous callers cannot list, see, sign or delete the files of a library, and a
/// signed link ends with the file or with the next change of its links.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class MediaLibrarySecurityTests(UploadsFixture fixture)
{
    private async Task<(string Token, long Id, string Thumb)> LibraryFileAsync()
    {
        var token = NewMemberToken();
        using var response = await UploadToLibraryAsync(fixture.Client, token, Jpeg(500, 400));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = await BodyAsync(response);
        return (token, item.GetProperty("id").GetInt64(), item.GetProperty("image").GetProperty("thumb").GetString()!);
    }

    [Fact]
    public async Task GivenNoToken_WhenUsingTheLibrary_ThenEveryRouteAnswers401()
    {
        var (_, id, _) = await LibraryFileAsync();
        using var list = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/media", null);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        using var detail = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{id}", null);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        using var delete = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{id}", null);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
        using var upload = await UploadToLibraryAsync(fixture.Client, null, Jpeg(300, 200));
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.MediaItems.AnyAsync(m => m.Id == id));
    }

    [Fact]
    public async Task GivenAnotherMember_WhenListingSeeingOrDeletingAFile_ThenItAnswersLikeForAMissingFileAndNothingChanges()
    {
        var (_, id, _) = await LibraryFileAsync();
        var stranger = NewMemberToken();

        using var list = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/media", stranger);
        var body = await BodyAsync(list);
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.DoesNotContain(id.ToString(), await list.Content.ReadAsStringAsync());

        using var detail = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{id}", stranger);
        await AssertProblemAsync(detail, HttpStatusCode.NotFound, "media_not_found");
        using var missingDetail = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/media/9223372036854775000", stranger);
        await AssertProblemAsync(missingDetail, HttpStatusCode.NotFound, "media_not_found");

        using var delete = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{id}", stranger);
        await AssertProblemAsync(delete, HttpStatusCode.NotFound, "media_not_found");
        using var missingDelete = await SendAsync(fixture.Client, HttpMethod.Delete, "/api/me/media/9223372036854775000", stranger);
        await AssertProblemAsync(missingDelete, HttpStatusCode.NotFound, "media_not_found");

        await using var db = fixture.CreateDbContext();
        var item = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.Id == id);
        Assert.True(await fixture.ObjectStore.ExistsAsync(item.StorageKey));
        Assert.All(item.Variants, v => Assert.StartsWith("private/", v.StorageKey));
    }

    [Fact]
    public async Task GivenAFileLinkedToATankOfAnotherMember_ThenOnlyTheOwnerSeesItAndTheKindOfThePlaceWithoutItsNameOrId()
    {
        var (owner, id, _) = await LibraryFileAsync();
        var (stranger, tank) = await OwnTankAsync(fixture.Client);
        await using (var db = fixture.CreateDbContext())
        {
            db.MediaLinks.Add(MediaLink.ForTank(tank, id, MediaLinkRole.Showcase));
            await db.SaveChangesAsync();
        }

        using var strangerList = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/media", stranger);
        Assert.Equal(0, (await BodyAsync(strangerList)).GetProperty("total").GetInt32());
        using var strangerDelete = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{id}", stranger);
        await AssertProblemAsync(strangerDelete, HttpStatusCode.NotFound, "media_not_found");

        using var ownerDetail = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{id}", owner);
        var place = Assert.Single((await BodyAsync(ownerDetail)).GetProperty("places").EnumerateArray());
        Assert.Equal("tank", place.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKindOf.Null, place.GetProperty("name").ValueKind.ToString());
        Assert.Equal(JsonValueKindOf.Null, place.GetProperty("tankId").ValueKind.ToString());
    }

    [Fact]
    public async Task GivenASignedLinkOfALibraryFile_WhenTheFileIsDeleted_ThenTheRouteRefusesIt()
    {
        var (owner, id, thumb) = await LibraryFileAsync();
        using (var served = await fixture.Client.GetAsync(thumb)) Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{id}", owner);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        using var refused = await fixture.Client.GetAsync(thumb);
        await AssertProblemAsync(refused, HttpStatusCode.NotFound, "media_not_found");
    }

    [Fact]
    public async Task GivenASignedLinkOfALibraryFile_WhenALinkIsCreatedOrRemoved_ThenTheGenerationChangesAndTheOldLinkIsRefused()
    {
        var (owner, id, thumb) = await LibraryFileAsync();
        long first;
        await using (var db = fixture.CreateDbContext()) first = await db.MediaItems.Where(m => m.Id == id).Select(m => m.VisibilityGeneration).SingleAsync();

        var tank = await CreateTankAsync(fixture.Client, owner);
        long linkId;
        await using (var db = fixture.CreateDbContext())
        {
            var link = MediaLink.ForTank(tank, id, MediaLinkRole.Showcase);
            db.MediaLinks.Add(link);
            await db.SaveChangesAsync();
            linkId = link.Id;
        }

        using (var afterCreate = await fixture.Client.GetAsync(thumb))
            await AssertProblemAsync(afterCreate, HttpStatusCode.NotFound, "media_not_found");
        long second;
        await using (var db = fixture.CreateDbContext()) second = await db.MediaItems.Where(m => m.Id == id).Select(m => m.VisibilityGeneration).SingleAsync();
        Assert.True(second > first);

        using var detail = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{id}", owner);
        var fresh = (await BodyAsync(detail)).GetProperty("item").GetProperty("image").GetProperty("thumb").GetString()!;
        using (var served = await fixture.Client.GetAsync(fresh)) Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        await using (var db = fixture.CreateDbContext())
            await db.MediaLinks.Where(l => l.Id == linkId).ExecuteDeleteAsync();
        using (var afterRemove = await fixture.Client.GetAsync(fresh))
            await AssertProblemAsync(afterRemove, HttpStatusCode.NotFound, "media_not_found");
        await using var after = fixture.CreateDbContext();
        Assert.True(await after.MediaItems.Where(m => m.Id == id).Select(m => m.VisibilityGeneration).SingleAsync() > second);
    }

    private static class JsonValueKindOf
    {
        public const string Null = "Null";
    }
}
