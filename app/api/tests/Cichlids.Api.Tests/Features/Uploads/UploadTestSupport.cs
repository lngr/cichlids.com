using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Api.Features.Uploads;
using NetVips;

namespace Cichlids.Api.Tests.Features.Uploads;

/// <summary>
/// Synthetic test images generated with libvips and thin request helpers for the upload,
/// publish, discard and drafts endpoints.
/// </summary>
internal static class UploadTestSupport
{
    public static byte[] Jpeg(int width, int height)
    {
        using var image = Image.Black(width, height) + new[] { 120, 40, 200 };
        return image.WriteToBuffer(".jpg", new VOption { { "Q", 90 } });
    }

    public static byte[] Png(int width, int height)
    {
        using var image = Image.Black(width, height) + new[] { 20, 160, 90 };
        return image.WriteToBuffer(".png");
    }

    /// <summary>
    /// A single-band, flat black JPEG. Flat content compresses to a few hundred kilobytes even at
    /// very large dimensions, which makes it the cheap way to exceed a pixel-count limit.
    /// </summary>
    public static byte[] FlatJpeg(int width, int height)
    {
        using var image = Image.Black(width, height);
        return image.WriteToBuffer(".jpg", new VOption { { "Q", 50 } });
    }

    public static byte[] Webp(int width, int height)
    {
        using var image = Image.Black(width, height) + new[] { 200, 120, 40 };
        return image.WriteToBuffer(".webp");
    }

    /// <summary>
    /// A JPEG whose raw pixel grid is width by height, tagged with EXIF orientation 6 (rotate 90
    /// degrees), so a viewer that honors orientation sees width and height swapped.
    /// </summary>
    public static byte[] OrientedJpeg(int width, int height)
    {
        using var image = Image.Black(width, height) + new[] { 120, 40, 200 };
        using var tagged = image.Mutate(m => m.Set(GValue.GIntType, "orientation", 6));
        return tagged.WriteToBuffer(".jpg", new VOption { { "Q", 90 } });
    }

    /// <summary>
    /// Bytes that start with the JPEG signature but carry no decodable JPEG data after it, so
    /// they pass a signature check and fail only in the decoder.
    /// </summary>
    public static byte[] UndecodableJpeg() => [0xFF, 0xD8, 0xFF, 0xE0, .. "not a real jpeg"u8.ToArray()];

    public static string NewToken() => NewCaller().Token;

    /// <summary>
    /// A fresh caller: a token for a subject no profile exists for yet, and that subject, so a
    /// test can look up the profile the first authenticated call creates.
    /// </summary>
    public static (string Token, string Subject) NewCaller()
    {
        var subject = Guid.NewGuid().ToString();
        return (TestTokens.Create(subject, $"uploader-{Guid.NewGuid():N}"[..24]), subject);
    }

    public static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, string? token, byte[] bytes, string contentType, string fileName = "photo.jpg")
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/uploads") { Content = content };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    public static async Task<DraftDto> UploadDraftAsync(HttpClient client, string token, byte[] bytes, string contentType = "image/jpeg")
    {
        var response = await UploadAsync(client, token, bytes, contentType);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<DraftDto>(response);
    }

    public static async Task<HttpResponseMessage> PublishAsync(HttpClient client, string token, long postId, PublishPostRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/posts/{postId}/publish")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> DiscardAsync(HttpClient client, string token, long postId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/posts/{postId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> ListDraftsAsync(HttpClient client, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me/drafts");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;
}
