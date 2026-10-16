using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// The outbox events of the video pipeline. Each is added to the context so it commits with the
/// state change that raised it.
/// </summary>
public static class VideoEvents
{
    public const string UploadCompleted = "video.upload_completed";
    public const string Processed = "video.processed";
    public const string Failed = "video.failed";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public static void Add(CichlidsDbContext context, string eventType, long mediaItemId, long? profileId, DateTimeOffset now, object? details = null) =>
        context.OutboxEvents.Add(OutboxEvent.Create(
            eventType, "video", mediaItemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { mediaItemId, profileId, details }, PayloadOptions), now));
}
