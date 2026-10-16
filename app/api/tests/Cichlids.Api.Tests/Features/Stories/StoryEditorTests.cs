// Story: TASK-5.20.5
using System.Net;
using System.Text.Json;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeciesEntity = Cichlids.Domain.Entities.Species;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Library.MediaLibraryTests;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;
using static Cichlids.Api.Tests.Features.Videos.VideoTestSupport;

namespace Cichlids.Api.Tests.Features.Stories;

/// <summary>
/// Sharing a file to the gallery and editing stories: quick share once per file, the editor for items,
/// captions, order, cover and subjects, publishing, withdrawing and deleting.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class StoryEditorTests(UploadsFixture fixture)
{
    internal async Task<long> LibraryPhotoAsync(string token)
    {
        using var response = await UploadToLibraryAsync(fixture.Client, token, Jpeg(500, 400));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    internal async Task<JsonElement> NewDraftAsync(string token, string? title = "Editor story")
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, "/api/me/stories", token, new { title, description = "Text", topic = "cichlids" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await BodyAsync(response);
    }

    internal async Task<JsonElement> AddItemAsync(string token, long story, long mediaItem, string? caption = null)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/items", token, new { mediaItemId = mediaItem, caption });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task<JsonElement> EditorAsync(string token, long story)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/stories/{story}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task<JsonElement> PublishDraftAsync(string token, long story)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/stories/{story}/publish", token, new { title = "Published", description = "Text", topic = "cichlids" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    private static List<long> ItemIds(JsonElement editor) => [.. editor.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64())];

    private async Task<string> LibraryDetailAsync(string token, long mediaItem)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{mediaItem}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<long> VisibilityGenerationAsync(long mediaItem)
    {
        await using var db = fixture.CreateDbContext();
        return await db.MediaItems.Where(m => m.Id == mediaItem).Select(m => m.VisibilityGeneration).SingleAsync();
    }

    private async Task<long> NewSpeciesAsync()
    {
        await using var db = fixture.CreateDbContext();
        var species = new SpeciesEntity { Genus = $"Editor{Guid.NewGuid():N}", Name = "test", DisplayName = "Editor test" };
        db.Species.Add(species);
        await db.SaveChangesAsync();
        return species.Id;
    }

    [Fact]
    public async Task GivenAnOwnLibraryPhoto_WhenSharedToTheGallery_ThenAPublishedSingleItemStoryExistsAndTheFileLinksToIt()
    {
        var token = NewMemberToken();
        var media = await LibraryPhotoAsync(token);

        using var shared = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", token, new { title = "My first cichlid", topic = "cichlids" });

        Assert.Equal(HttpStatusCode.Created, shared.StatusCode);
        var story = await BodyAsync(shared);
        var slug = story.GetProperty("slug").GetString()!;
        Assert.NotEmpty(slug);
        Assert.Single(story.GetProperty("items").EnumerateArray());
        using var inGallery = await fixture.Client.GetAsync($"/api/stories/{slug}");
        Assert.Equal(HttpStatusCode.OK, inGallery.StatusCode);
        var detail = JsonDocument.Parse(await LibraryDetailAsync(token, media)).RootElement;
        var quickShare = detail.GetProperty("item").GetProperty("quickShare");
        Assert.Equal(slug, quickShare.GetProperty("slug").GetString());
        Assert.Equal(story.GetProperty("id").GetInt64(), quickShare.GetProperty("storyId").GetInt64());
    }

    [Fact]
    public async Task GivenAPhotoSharedBefore_WhenSharedAgain_ThenItIsRefusedAndNoSecondStoryExists()
    {
        var token = NewMemberToken();
        var media = await LibraryPhotoAsync(token);
        using var first = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", token, new { title = "Once" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", token, new { title = "Twice" });

        await AssertProblemAsync(second, HttpStatusCode.Conflict, "media_already_shared");
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.MediaLinks.CountAsync(l => l.MediaItemId == media && l.StoryId != null));
    }

    [Fact]
    public async Task GivenAFileOfAnotherMember_WhenSharedOrAddedToAStory_ThenItAnswersLikeAMissingFileAndNothingChanges()
    {
        var owner = NewMemberToken();
        var media = await LibraryPhotoAsync(owner);
        var stranger = NewMemberToken();
        var draft = await NewDraftAsync(stranger);
        var draftId = draft.GetProperty("id").GetInt64();

        using var share = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", stranger, new { title = "Mine now" });
        await AssertProblemAsync(share, HttpStatusCode.NotFound, "media_not_found");
        using var add = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{draftId}/items", stranger, new { mediaItemId = media });
        await AssertProblemAsync(add, HttpStatusCode.NotFound, "media_not_found");
        using var missing = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{draftId}/items", stranger, new { mediaItemId = 9223372036854775000 });
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "media_not_found");

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaLinks.AnyAsync(l => l.MediaItemId == media));
        Assert.Null(await db.MediaItems.Where(m => m.Id == media).Select(m => m.QuickShareStoryId).SingleAsync());
    }

    [Fact]
    public async Task GivenAQuickSharedPhoto_WhenAddedToASecondStoryWithAnotherCaption_ThenBothStoriesShowItWithTheirOwnCaption()
    {
        var token = NewMemberToken();
        var media = await LibraryPhotoAsync(token);
        using var shared = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", token, new { title = "First" });
        var first = await BodyAsync(shared);
        var captionLink = first.GetProperty("items")[0].GetProperty("id").GetInt64();
        using var caption = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/stories/{first.GetProperty("id").GetInt64()}/items/{captionLink}", token, new { caption = "Caption one" });
        Assert.Equal(HttpStatusCode.OK, caption.StatusCode);

        var second = await NewDraftAsync(token);
        var secondId = second.GetProperty("id").GetInt64();
        await AddItemAsync(token, secondId, media, "Caption two");
        var published = await PublishDraftAsync(token, secondId);

        using var firstDetail = await fixture.Client.GetAsync($"/api/stories/{first.GetProperty("slug").GetString()}");
        Assert.Equal("Caption one", (await BodyAsync(firstDetail)).GetProperty("items")[0].GetProperty("caption").GetString());
        Assert.Equal("Caption two", published.GetProperty("items")[0].GetProperty("caption").GetString());
        Assert.Equal(media, published.GetProperty("items")[0].GetProperty("mediaItemId").GetInt64());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.MediaLinks.CountAsync(l => l.MediaItemId == media && l.StoryId != null));
    }

    [Fact]
    public async Task GivenADraftWithItemsATankCardAndAChosenCover_WhenPublished_ThenTheGalleryListsItWithThatCoverAndTheCardInOrder()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var a = await LibraryPhotoAsync(token);
        var b = await LibraryPhotoAsync(token);
        var draft = await NewDraftAsync(token);
        var story = draft.GetProperty("id").GetInt64();
        await AddItemAsync(token, story, a, "A");
        var withItems = await AddItemAsync(token, story, b, "B");
        var coverLink = ItemIds(withItems)[1];

        using var subject = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/subjects", token, new { type = "tank", id = tank, positioned = true });
        Assert.Equal(HttpStatusCode.OK, subject.StatusCode);
        using var cover = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/stories/{story}/cover", token, new { itemId = coverLink });
        Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
        var published = await PublishDraftAsync(token, story);

        Assert.Equal(coverLink, published.GetProperty("coverItemId").GetInt64());
        var slug = published.GetProperty("slug").GetString()!;
        using var list = await fixture.Client.GetAsync($"/api/stories?user={(await ProfileIdAsync(story))}");
        var entry = (await BodyAsync(list)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("slug").GetString() == slug);
        Assert.Equal(published.GetProperty("items")[1].GetProperty("image").GetProperty("thumb").GetString()?.Split('/').Last().Split('?')[0],
            entry.GetProperty("cover").GetProperty("thumb").GetString()?.Split('/').Last().Split('?')[0]);
        Assert.Equal(2, entry.GetProperty("itemCount").GetInt32());
        var card = Assert.Single(published.GetProperty("subjects").EnumerateArray());
        Assert.Equal("tank", card.GetProperty("type").GetString());
        Assert.Equal(2, card.GetProperty("position").GetInt32());
    }

    private async Task<long> ProfileIdAsync(long story)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Stories.Where(s => s.Id == story).Select(s => s.AuthorProfileId).SingleAsync();
    }

    [Fact]
    public async Task GivenADraftWithTwoItemsAndACard_WhenReordered_ThenItemsAndCardShareOneDenseSequence()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var draft = await NewDraftAsync(token);
        var story = draft.GetProperty("id").GetInt64();
        await AddItemAsync(token, story, await LibraryPhotoAsync(token));
        var withTwo = await AddItemAsync(token, story, await LibraryPhotoAsync(token));
        using var subject = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/subjects", token, new { type = "tank", id = tank, positioned = true });
        var editor = await BodyAsync(subject);
        var items = ItemIds(withTwo);
        var subjectId = editor.GetProperty("subjects")[0].GetProperty("id").GetInt64();

        using var reordered = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/stories/{story}/order", token, new
        {
            entries = new[] { new { kind = "subject", id = subjectId }, new { kind = "item", id = items[1] }, new { kind = "item", id = items[0] } },
        });

        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        var after = await BodyAsync(reordered);
        Assert.Equal([items[1], items[0]], ItemIds(after));
        Assert.Equal([1, 2], after.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("position").GetInt32()));
        Assert.Equal(0, after.GetProperty("subjects")[0].GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task GivenAnOrderThatMissesAnEntryRepeatsOneOrNamesAForeignOne_ThenItIsRefusedAndTheOrderStays()
    {
        var token = NewMemberToken();
        var draft = await NewDraftAsync(token);
        var story = draft.GetProperty("id").GetInt64();
        await AddItemAsync(token, story, await LibraryPhotoAsync(token));
        var two = await AddItemAsync(token, story, await LibraryPhotoAsync(token));
        var items = ItemIds(two);

        foreach (var entries in new[]
        {
            new[] { new { kind = "item", id = items[0] } },
            [new { kind = "item", id = items[0] }, new { kind = "item", id = items[0] }],
            [new { kind = "item", id = items[0] }, new { kind = "item", id = items[1] + 100000 }],
            [new { kind = "item", id = items[0] }, new { kind = "subject", id = items[1] }],
        })
        {
            using var response = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/stories/{story}/order", token, new { entries });
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "story_order_invalid");
        }

        Assert.Equal(items, ItemIds(await EditorAsync(token, story)));
    }

    [Fact]
    public async Task GivenACoverThatIsNotAnItemOfTheStory_ThenItIsRefusedAndNullChoosesTheFirstItem()
    {
        var token = NewMemberToken();
        var one = (await NewDraftAsync(token)).GetProperty("id").GetInt64();
        var other = (await NewDraftAsync(token)).GetProperty("id").GetInt64();
        var linkOfOther = ItemIds(await AddItemAsync(token, other, await LibraryPhotoAsync(token)))[0];
        var mine = ItemIds(await AddItemAsync(token, one, await LibraryPhotoAsync(token)))[0];

        using var foreign = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/stories/{one}/cover", token, new { itemId = linkOfOther });
        await AssertProblemAsync(foreign, HttpStatusCode.BadRequest, "story_cover_invalid");
        using var chosen = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/stories/{one}/cover", token, new { itemId = mine });
        Assert.Equal(mine, (await BodyAsync(chosen)).GetProperty("coverItemId").GetInt64());
        using var cleared = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/stories/{one}/cover", token, new { itemId = (long?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal(mine, (await BodyAsync(cleared)).GetProperty("coverItemId").GetInt64());
        await using var db = fixture.CreateDbContext();
        Assert.Null(await db.Stories.Where(s => s.Id == one).Select(s => s.CoverMediaLinkId).SingleAsync());
    }

    [Fact]
    public async Task GivenAnItemRemovedFromAStory_ThenOnlyTheLinkGoesAndTheFileStaysInTheLibraryAndPositionsStayDense()
    {
        var token = NewMemberToken();
        var story = (await NewDraftAsync(token)).GetProperty("id").GetInt64();
        var a = await LibraryPhotoAsync(token);
        await AddItemAsync(token, story, a);
        await AddItemAsync(token, story, await LibraryPhotoAsync(token));
        var three = await AddItemAsync(token, story, await LibraryPhotoAsync(token));
        var items = ItemIds(three);

        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/stories/{story}/items/{items[0]}", token);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var after = await BodyAsync(removed);
        Assert.Equal([items[1], items[2]], ItemIds(after));
        Assert.Equal([0, 1], after.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("position").GetInt32()));
        using var file = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{a}", token);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
    }

    [Fact]
    public async Task GivenSubjectsOfEveryType_WhenAddedWithAndWithoutPosition_ThenTheyAreRecordedAndDuplicatesAreRefused()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var species = await NewSpeciesAsync();
        var story = (await NewDraftAsync(token)).GetProperty("id").GetInt64();
        await AddItemAsync(token, story, await LibraryPhotoAsync(token));

        using var speciesReference = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/subjects", token, new { type = "species", id = species, positioned = false });
        using var tankCard = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/subjects", token, new { type = "tank", id = tank, positioned = true });
        using var duplicate = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/subjects", token, new { type = "tank", id = tank, positioned = true });

        Assert.Equal(HttpStatusCode.OK, speciesReference.StatusCode);
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "story_subject_exists");
        var subjects = (await BodyAsync(tankCard)).GetProperty("subjects").EnumerateArray().ToList();
        Assert.Equal("tank", subjects[0].GetProperty("type").GetString());
        Assert.Equal(1, subjects[0].GetProperty("position").GetInt32());
        Assert.Equal(JsonValueKind.Null, subjects[1].GetProperty("position").ValueKind);

        var cardId = subjects[0].GetProperty("id").GetInt64();
        using var demoted = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/stories/{story}/subjects/{cardId}", token, new { positioned = false });
        Assert.All((await BodyAsync(demoted)).GetProperty("subjects").EnumerateArray(), s => Assert.Equal(JsonValueKind.Null, s.GetProperty("position").ValueKind));
        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/stories/{story}/subjects/{cardId}", token);
        Assert.Single((await BodyAsync(removed)).GetProperty("subjects").EnumerateArray());
    }

    [Fact]
    public async Task GivenATankThatTheAuthorMaySee_WhenItIsAnotherMembersPublicTank_ThenItCanBeASubject()
    {
        var (_, othersTank) = await OwnTankAsync(fixture.Client);
        var token = NewMemberToken();
        var story = (await NewDraftAsync(token)).GetProperty("id").GetInt64();

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/subjects", token, new { type = "tank", id = othersTank, positioned = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(othersTank, (await BodyAsync(response)).GetProperty("subjects")[0].GetProperty("tank").GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task GivenAPublishedStory_WhenWithdrawn_ThenItIsADraftTheGalleryHidesItAndPublishingAgainKeepsTheAddress()
    {
        var token = NewMemberToken();
        var media = await LibraryPhotoAsync(token);
        using var shared = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", token, new { title = "Back and forth" });
        var story = await BodyAsync(shared);
        var id = story.GetProperty("id").GetInt64();
        var slug = story.GetProperty("slug").GetString()!;
        var generation = await VisibilityGenerationAsync(media);

        using var withdrawn = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{id}/withdraw", token);

        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Equal("draft", (await BodyAsync(withdrawn)).GetProperty("state").GetString());
        using var hidden = await fixture.Client.GetAsync($"/api/stories/{slug}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.True(await VisibilityGenerationAsync(media) > generation);

        var again = await PublishDraftAsync(token, id);
        Assert.Equal(slug, again.GetProperty("slug").GetString());
        using var visible = await fixture.Client.GetAsync($"/api/stories/{slug}");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
    }

    [Fact]
    public async Task GivenASignedLinkOfAPhotoInADraft_WhenTheStoryIsPublishedAndWithdrawn_ThenTheOldLinkIsRefusedAndAFreshOneWorksForTheAuthorOnly()
    {
        var token = NewMemberToken();
        var story = (await NewDraftAsync(token)).GetProperty("id").GetInt64();
        var editor = await AddItemAsync(token, story, await LibraryPhotoAsync(token));
        var oldLink = editor.GetProperty("items")[0].GetProperty("image").GetProperty("thumb").GetString()!;
        using (var served = await fixture.Client.GetAsync(oldLink)) Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        await PublishDraftAsync(token, story);
        using var withdrawn = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/withdraw", token);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);

        using var refused = await fixture.Client.GetAsync(oldLink);
        await AssertProblemAsync(refused, HttpStatusCode.NotFound, "media_not_found");
        var fresh = (await BodyAsync(withdrawn)).GetProperty("items")[0].GetProperty("image").GetProperty("thumb").GetString()!;
        using var authorServed = await fixture.Client.GetAsync(fresh);
        Assert.Equal(HttpStatusCode.OK, authorServed.StatusCode);
        using var stranger = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/stories/{story}", NewMemberToken());
        await AssertProblemAsync(stranger, HttpStatusCode.NotFound, "story_not_found");
    }

    [Fact]
    public async Task GivenAPublishedStory_WhenDeleted_ThenTheGalleryDropsItTheGenerationChangesAndTheFileCanBeSharedAgain()
    {
        var token = NewMemberToken();
        var media = await LibraryPhotoAsync(token);
        using var shared = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", token, new { title = "Gone soon" });
        var story = await BodyAsync(shared);
        var generation = await VisibilityGenerationAsync(media);

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/stories/{story.GetProperty("id").GetInt64()}", token);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var hidden = await fixture.Client.GetAsync($"/api/stories/{story.GetProperty("slug").GetString()}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.True(await VisibilityGenerationAsync(media) > generation);
        using var file = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{media}", token);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        using var again = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", token, new { title = "Back" });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task GivenADraftWithItems_WhenDeleted_ThenTheStoryAndItsLinksGoAndTheFilesStayInTheLibrary()
    {
        var token = NewMemberToken();
        var story = (await NewDraftAsync(token)).GetProperty("id").GetInt64();
        var media = await LibraryPhotoAsync(token);
        await AddItemAsync(token, story, media);
        var generation = await VisibilityGenerationAsync(media);

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/stories/{story}", token);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Stories.AnyAsync(s => s.Id == story));
        Assert.False(await db.MediaLinks.AnyAsync(l => l.MediaItemId == media));
        Assert.True(await VisibilityGenerationAsync(media) > generation);
    }

    [Fact]
    public async Task GivenAStoryWithoutItems_WhenPublishedOrItsLastItemOfAPublishedStoryIsRemoved_ThenItIsRefused()
    {
        var token = NewMemberToken();
        var empty = (await NewDraftAsync(token)).GetProperty("id").GetInt64();
        using var noItems = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/stories/{empty}/publish", token, new { title = "Nothing" });
        await AssertProblemAsync(noItems, HttpStatusCode.Conflict, "story_needs_item");

        var story = (await NewDraftAsync(token)).GetProperty("id").GetInt64();
        var link = ItemIds(await AddItemAsync(token, story, await LibraryPhotoAsync(token)))[0];
        await PublishDraftAsync(token, story);
        using var removal = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/stories/{story}/items/{link}", token);
        await AssertProblemAsync(removal, HttpStatusCode.Conflict, "story_needs_item");
    }

    [Fact]
    public async Task GivenAnotherMember_WhenUsingTheEditorOfAStory_ThenEveryRouteAnswersLikeForAMissingStoryAndNothingChanges()
    {
        var owner = NewMemberToken();
        var story = (await NewDraftAsync(owner)).GetProperty("id").GetInt64();
        var withItem = await AddItemAsync(owner, story, await LibraryPhotoAsync(owner));
        var link = ItemIds(withItem)[0];
        var stranger = NewMemberToken();
        var own = await LibraryPhotoAsync(stranger);
        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, $"/api/me/stories/{story}", null),
            (HttpMethod.Put, $"/api/me/stories/{story}", new { title = "Hijacked" }),
            (HttpMethod.Delete, $"/api/me/stories/{story}", null),
            (HttpMethod.Post, $"/api/me/stories/{story}/withdraw", null),
            (HttpMethod.Post, $"/api/me/stories/{story}/items", new { mediaItemId = own }),
            (HttpMethod.Put, $"/api/me/stories/{story}/items/{link}", new { caption = "x" }),
            (HttpMethod.Delete, $"/api/me/stories/{story}/items/{link}", null),
            (HttpMethod.Put, $"/api/me/stories/{story}/order", new { entries = new[] { new { kind = "item", id = link } } }),
            (HttpMethod.Put, $"/api/me/stories/{story}/cover", new { itemId = link }),
            (HttpMethod.Post, $"/api/me/stories/{story}/subjects", new { type = "species", id = 1, positioned = false }),
        };

        foreach (var (method, url, body) in calls)
        {
            using var response = await SendAsync(fixture.Client, method, url, stranger, body);
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "story_not_found");
        }

        using var publish = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/stories/{story}/publish", stranger, new { title = "Hijacked" });
        Assert.Equal(HttpStatusCode.NotFound, publish.StatusCode);
        var unchanged = await EditorAsync(owner, story);
        Assert.Equal("Editor story", unchanged.GetProperty("title").GetString());
        Assert.Equal("draft", unchanged.GetProperty("state").GetString());
        Assert.Equal([link], ItemIds(unchanged));
    }

    [Fact]
    public async Task GivenNoToken_WhenUsingTheEditor_ThenEveryRouteAnswers401()
    {
        foreach (var (method, url) in new[]
        {
            (HttpMethod.Get, "/api/me/stories"), (HttpMethod.Post, "/api/me/stories"), (HttpMethod.Get, "/api/me/stories/1"),
            (HttpMethod.Delete, "/api/me/stories/1"), (HttpMethod.Post, "/api/me/media/1/share"), (HttpMethod.Put, "/api/me/stories/1/order"),
        })
        {
            using var response = await SendAsync(fixture.Client, method, url, null, method == HttpMethod.Get ? null : new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task GivenAPrivateTankOfAnotherMemberAndAMissingTank_WhenUsedAsSubjects_ThenBothAnswerTheSameAndNothingIsRecorded()
    {
        var (_, privateTank) = await OwnTankAsync(fixture.Client, isPublic: false);
        var token = NewMemberToken();
        var story = (await NewDraftAsync(token)).GetProperty("id").GetInt64();

        using var hidden = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/subjects", token, new { type = "tank", id = privateTank, positioned = true });
        using var missing = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/subjects", token, new { type = "tank", id = 9223372036854775000, positioned = true });

        await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "story_subject_not_found");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "story_subject_not_found");
        Assert.Equal((await BodyAsync(missing)).GetProperty("detail").GetString(), (await BodyAsync(hidden)).GetProperty("detail").GetString());
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.StorySubjects.AnyAsync(s => s.StoryId == story));
    }

    [Fact]
    public async Task GivenAnInhabitantOfAPrivateTankOfAnotherMember_WhenUsedAsASubject_ThenItIsRefusedLikeAMissingOne()
    {
        var (owner, tank) = await OwnTankAsync(fixture.Client, isPublic: false);
        await using (var db = fixture.CreateDbContext())
        {
            var species = new SpeciesEntity { Genus = $"Hidden{Guid.NewGuid():N}", Name = "test", DisplayName = "Hidden test" };
            db.Species.Add(species);
            await db.SaveChangesAsync();
            db.Inhabitants.Add(new Inhabitant { TankId = tank, SpeciesId = species.Id, Count = 2, Sort = 0 });
            await db.SaveChangesAsync();
        }

        long inhabitant;
        await using (var db = fixture.CreateDbContext()) inhabitant = await db.Inhabitants.Where(i => i.TankId == tank).Select(i => i.Id).SingleAsync();
        var stranger = NewMemberToken();
        var story = (await NewDraftAsync(stranger)).GetProperty("id").GetInt64();

        using var refused = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/subjects", stranger, new { type = "inhabitant", id = inhabitant, positioned = false });
        await AssertProblemAsync(refused, HttpStatusCode.NotFound, "story_subject_not_found");

        var storyOfOwner = (await NewDraftAsync(owner)).GetProperty("id").GetInt64();
        using var accepted = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{storyOfOwner}/subjects", owner, new { type = "inhabitant", id = inhabitant, positioned = false });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task GivenTheListOfMyStories_ThenItShowsDraftsAndPublishedStoriesOfTheCallerOnly()
    {
        var token = NewMemberToken();
        var draft = (await NewDraftAsync(token, "A draft")).GetProperty("id").GetInt64();
        using var shared = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{await LibraryPhotoAsync(token)}/share", token, new { title = "Shared" });
        var sharedId = (await BodyAsync(shared)).GetProperty("id").GetInt64();
        await NewDraftAsync(NewMemberToken(), "Not mine");

        using var response = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/stories", token);

        var list = await BodyAsync(response);
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        var ids = list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
        Assert.Contains(draft, ids);
        Assert.Contains(sharedId, ids);
    }

    [Fact]
    public async Task GivenAVideo_WhenItIsStillProcessing_ThenItCannotBeSharedOrAddedAndAfterwardsItCan()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var uploaded = await UploadedAsync(fixture.Client, token, await SmallVideo, tank);
        var video = uploaded.GetProperty("id").GetInt64();
        var story = (await NewDraftAsync(token)).GetProperty("id").GetInt64();

        using var early = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{video}/share", token, new { title = "Too early" });
        await AssertProblemAsync(early, HttpStatusCode.Conflict, "media_not_ready");
        using var earlyAdd = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/items", token, new { mediaItemId = video });
        await AssertProblemAsync(earlyAdd, HttpStatusCode.Conflict, "media_not_ready");

        await ProcessQueueAsync(fixture.Services);
        using var ready = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{video}/share", token, new { title = "A video" });

        Assert.Equal(HttpStatusCode.Created, ready.StatusCode);
        Assert.Equal("video", (await BodyAsync(ready)).GetProperty("items")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GivenACarePhoto_ThenTheEntryNamesItsMediaItemAndTheOwnerCanShareIt()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var entry = await LogAsync(fixture.Client, token, tank, new { kind = "water_change", amount = new { value = 25, unit = "percent" } });
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg(600, 400));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "care.jpg");
        using var upload = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/care/{entry}/photos") { Content = content };
        upload.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var added = await fixture.Client.SendAsync(upload);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var photo = (await BodyAsync(added)).GetProperty("photos")[0];
        var media = photo.GetProperty("mediaItemId").GetInt64();

        await using (var db = fixture.CreateDbContext())
            Assert.Equal(media, await db.MediaLinks.Where(l => l.CareEntryId == entry).Select(l => l.MediaItemId).SingleAsync());
        using var shared = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", token, new { title = "Water change day" });
        Assert.Equal(HttpStatusCode.Created, shared.StatusCode);
    }

    [Fact]
    public async Task GivenADraftWithAChosenCover_WhenDeletedByTheAuthorOrDiscarded_ThenTheStoryGoes()
    {
        var token = NewMemberToken();
        foreach (var route in new[] { "/api/me/stories/{0}", "/api/stories/{0}" })
        {
            var story = (await NewDraftAsync(token)).GetProperty("id").GetInt64();
            var link = ItemIds(await AddItemAsync(token, story, await LibraryPhotoAsync(token)))[0];
            using var cover = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/stories/{story}/cover", token, new { itemId = link });
            Assert.Equal(HttpStatusCode.OK, cover.StatusCode);

            using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, string.Format(route, story), token);

            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            await using var db = fixture.CreateDbContext();
            Assert.False(await db.Stories.AnyAsync(s => s.Id == story));
        }
    }

    [Fact]
    public async Task GivenAWithdrawnStory_WhenDeleted_ThenItIsRemovedWithoutLinksAndKeepsItsAddress()
    {
        var token = NewMemberToken();
        var media = await LibraryPhotoAsync(token);
        using var shared = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", token, new { title = "Round trip" });
        var story = (await BodyAsync(shared)).GetProperty("id").GetInt64();
        using var withdrawn = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/withdraw", token);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/stories/{story}", token);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.NotNull(await db.Stories.Where(s => s.Id == story).Select(s => s.DeletedAt).SingleAsync());
        Assert.True(await db.SlugAliases.AnyAsync(a => a.StoryId == story));
        Assert.False(await db.MediaLinks.AnyAsync(l => l.StoryId == story));
    }

    [Fact]
    public async Task GivenAWithdrawnStory_WhenItsFileIsDeletedFromTheLibrary_ThenTheStoryIsRemovedAndTheDraftCleanupSkipsIt()
    {
        var token = NewMemberToken();
        var media = await LibraryPhotoAsync(token);
        using var shared = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/media/{media}/share", token, new { title = "Withdrawn" });
        var story = (await BodyAsync(shared)).GetProperty("id").GetInt64();
        using var withdrawn = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/stories/{story}/withdraw", token);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        await using (var db = fixture.CreateDbContext())
            await db.Stories.Where(s => s.Id == story).ExecuteUpdateAsync(set => set.SetProperty(s => s.UpdatedAt, DateTimeOffset.UtcNow.AddYears(-1)));
        using (var scope = fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Cichlids.Api.Features.Uploads.DraftCleanupService>().RunOnceAsync(CancellationToken.None);
        await using (var db = fixture.CreateDbContext())
            Assert.True(await db.Stories.AnyAsync(s => s.Id == story && s.DeletedAt == null));

        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{media}", token);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        await using var after = fixture.CreateDbContext();
        Assert.NotNull(await after.Stories.Where(s => s.Id == story).Select(s => s.DeletedAt).SingleAsync());
    }
}
