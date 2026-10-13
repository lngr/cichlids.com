using System.Text.Json;
using Cichlids.Api.Features.Pictures;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Tests.Features.Uploads;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Media;

/// <summary>
/// Every response that carries media URLs points at public variants only: no URL resolves to a
/// key under originals/, and no image object has an original property.
/// </summary>
[Collection(ApiCollection.Name)]
public class PublicResponsesExposeNoOriginalsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Read_endpoints_return_no_original_urls()
    {
        var seed = fixture.Seed;
        string[] paths =
        [
            "/api/pictures",
            $"/api/pictures/{seed.PublishedPictureACanonicalSlug}",
            "/api/tanks",
            $"/api/tanks/{seed.PublishedTank.Id}",
            $"/api/community/threads/{seed.CommunityCichlidsThreadA.Id}",
            $"/api/profiles/{seed.Bob.Id}",
        ];

        foreach (var path in paths)
        {
            var response = await fixture.Client.GetAsync(path);
            response.EnsureSuccessStatusCode();

            NoOriginalsAssert.Holds(path, await response.Content.ReadAsStringAsync(), ApiFixture.ExpectedPublicUrlPrefix);
        }
    }
}

/// <summary>
/// The upload response, the drafts list and the published picture of a real upload carry variant
/// URLs only.
/// </summary>
[Collection(UploadsCollection.Name)]
public class UploadResponsesExposeNoOriginalsTests(UploadsFixture fixture)
{
    [Fact]
    public async Task Upload_drafts_and_published_picture_return_no_original_urls()
    {
        var token = NewToken();

        var upload = await UploadAsync(fixture.Client, token, Jpeg(1000, 700), "image/jpeg", "reef.jpg");
        upload.EnsureSuccessStatusCode();
        var uploadJson = await upload.Content.ReadAsStringAsync();
        NoOriginalsAssert.Holds("upload", uploadJson, fixture.PublicBaseUrl);
        var draft = JsonSerializer.Deserialize<DraftDto>(uploadJson, TestJson.Options)!;
        Assert.StartsWith($"{fixture.PublicBaseUrl}/variants/", draft.Image.Thumb);

        var drafts = await ListDraftsAsync(fixture.Client, token);
        drafts.EnsureSuccessStatusCode();
        NoOriginalsAssert.Holds("drafts", await drafts.Content.ReadAsStringAsync(), fixture.PublicBaseUrl);

        var publish = await PublishAsync(
            fixture.Client, token, draft.Id, new PublishPostRequest("Frontosa", null, "cichlids"));
        publish.EnsureSuccessStatusCode();
        var published = await ReadAsync<PictureDetailDto>(publish);

        var detail = await fixture.Client.GetAsync($"/api/pictures/{published.Slug}");
        detail.EnsureSuccessStatusCode();
        NoOriginalsAssert.Holds("picture detail", await detail.Content.ReadAsStringAsync(), fixture.PublicBaseUrl);
    }
}

/// <summary>
/// Fails when a response body contains a URL to an object under originals/ or any JSON property
/// named original, at any depth.
/// </summary>
internal static class NoOriginalsAssert
{
    public static void Holds(string source, string json, string publicBaseUrl)
    {
        Assert.False(
            json.Contains($"{publicBaseUrl}/originals/", StringComparison.Ordinal),
            $"The {source} response contains an original URL: {json}");

        using var document = JsonDocument.Parse(json);
        var property = FindOriginalProperty(document.RootElement);
        Assert.True(property is null, $"The {source} response has an original property at {property}.");
    }

    private static string? FindOriginalProperty(JsonElement element, string path = "$")
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var propertyPath = $"{path}.{property.Name}";
                    if (property.Name.Equals("original", StringComparison.OrdinalIgnoreCase))
                    {
                        return propertyPath;
                    }

                    if (FindOriginalProperty(property.Value, propertyPath) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (FindOriginalProperty(item, $"{path}[{index++}]") is { } found)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }
}
