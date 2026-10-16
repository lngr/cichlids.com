using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Features.Videos;
using Cichlids.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.Videos;

internal static class VideoTestSupport
{
    public const int Chunk = 8 * 1024 * 1024;

    public static readonly Task<byte[]> SmallVideo = TestVideos.CreateAsync(seconds: 2);

    public static async Task<HttpResponseMessage> CreateSessionAsync(
        HttpClient client, string? token, long size, long? tankId = null, long? careEntryId = null, string fileName = "clip.mp4") =>
        await SendAsync(client, HttpMethod.Post, "/api/video-uploads", token, new
        {
            target = careEntryId is null ? "tank" : "care_entry", tankId, careEntryId, size, fileName,
        });

    public static async Task<long> OpenSessionAsync(HttpClient client, string token, long size, long tankId, long? careEntryId = null)
    {
        using var response = await CreateSessionAsync(client, token, size, tankId, careEntryId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    public static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string? token, long id, long offset, byte[] bytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/video-uploads/{id}") { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        request.Headers.Add("Upload-Offset", offset.ToString());
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> CompleteAsync(HttpClient client, string? token, long id) =>
        await SendAsync(client, HttpMethod.Post, $"/api/video-uploads/{id}/complete", token);

    /// <summary>
    /// Sends the whole file in chunks, completes the upload and returns the complete response.
    /// </summary>
    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, byte[] video, long tankId, long? careEntryId = null)
    {
        var id = await OpenSessionAsync(client, token, video.Length, tankId, careEntryId);
        for (long offset = 0; offset < video.Length; offset += Chunk)
        {
            using var patch = await PatchAsync(client, token, id, offset, video.AsMemory((int)offset, (int)Math.Min(Chunk, video.Length - offset)).ToArray());
            Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
        }

        return await CompleteAsync(client, token, id);
    }

    public static async Task<JsonElement> UploadedAsync(HttpClient client, string token, byte[] video, long tankId, long? careEntryId = null)
    {
        using var response = await UploadAsync(client, token, video, tankId, careEntryId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await BodyAsync(response);
    }

    /// <summary>
    /// Runs the transcoding queue of the given host until it is empty and returns the number of jobs processed.
    /// </summary>
    public static async Task<int> ProcessQueueAsync(IServiceProvider services)
    {
        var processed = 0;
        while (true)
        {
            using var scope = services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<VideoTranscodingService>().RunOnceAsync(CancellationToken.None))
            {
                return processed;
            }

            processed++;
        }
    }

    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        return body;
    }
}
