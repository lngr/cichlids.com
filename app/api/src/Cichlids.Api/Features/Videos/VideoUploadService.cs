using System.Security.Cryptography;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Cichlids.Infrastructure.Video;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// The resumable upload of a video. A session is opened for a target (a tank or a care entry of the
/// member) with the announced file size, which must not exceed the size limit the length policy
/// derives for the member. The file then arrives in chunks of the session's chunk size; each chunk
/// must start at the stored offset and have exactly the expected length (the chunk size, or the
/// rest of the file for the last one), and is stored as one part of a multipart upload in the
/// object store. The first chunk must carry an MP4 or QuickTime file type box. Completing the
/// upload assembles the original under the private prefix, probes it, applies the length policy and
/// records the media item, its transcoding job and the attachment in one transaction.
/// </summary>
public sealed class VideoUploadService(
    CichlidsDbContext context,
    IObjectStore store,
    VideoLimitPolicy policy,
    UploadsWriteService uploads,
    FfprobeClient probe,
    StoredObjectDeleter deleter,
    IOptions<VideoLimitOptions> options,
    TimeProvider clock,
    ILogger<VideoUploadService> logger)
{
    private const string Mp4ContentType = "video/mp4";

    /// <summary>
    /// Opens an upload session for the member.
    /// </summary>
    public async Task<(VideoUploadDto? Session, VideoProblem? Problem)> CreateAsync(
        long profileId, CreateVideoUploadRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (request.Size is not > 0 || request.TankId is null || !TryParseTarget(request.Target, out var target)
            || (target == VideoUploadTarget.CareEntry) != (request.CareEntryId is not null))
        {
            return (null, new VideoProblem(StatusCodes.Status400BadRequest, VideoProblem.UploadInvalid, "The upload request is incomplete."));
        }

        var tankId = request.TankId.Value;
        if (!await context.Tanks.AsNoTracking().AnyAsync(t => t.Id == tankId && t.ProfileId == profileId && t.DeletedAt == null, cancellationToken)
            || (request.CareEntryId is { } entryId && !await context.CareEntries.AsNoTracking()
                .AnyAsync(e => e.Id == entryId && e.TankId == tankId && e.AuthorProfileId == profileId, cancellationToken)))
        {
            return (null, new VideoProblem(StatusCodes.Status404NotFound, VideoProblem.TargetNotFound, "The tank or care entry is unavailable."));
        }

        if (request.CareEntryId is { } careEntry
            && await context.MediaLinks.CountAsync(l => l.CareEntryId == careEntry && l.Role == MediaLinkRole.Video, cancellationToken) >= MediaLinkLimits.CareVideosPerEntry)
        {
            return (null, new VideoProblem(StatusCodes.Status409Conflict, VideoProblem.CareVideoLimitReached, "The care entry holds the most videos it can."));
        }

        if (await uploads.CheckHourlyQuotaAsync(profileId, cancellationToken) is not null)
        {
            return (null, new VideoProblem(StatusCodes.Status429TooManyRequests, UploadsEndpoints.UploadRateLimitedCode, "The hourly upload limit is reached."));
        }

        var limits = await policy.ResolveAsync(profileId, cancellationToken);
        var now = clock.GetUtcNow();
        if (await context.VideoUploads.CountAsync(u => u.OwnerProfileId == profileId && u.ExpiresAt > now, cancellationToken) >= limits.MaxOpenUploads)
        {
            return (null, new VideoProblem(StatusCodes.Status409Conflict, VideoProblem.UploadLimitReached, "Finish or discard an open video upload first."));
        }

        if (limits.MaxVideos is { } maxVideos
            && await context.MediaItems.CountAsync(m => m.OwnerProfileId == profileId && m.Kind == MediaKind.Video, cancellationToken) >= maxVideos)
        {
            return (null, new VideoProblem(StatusCodes.Status409Conflict, VideoProblem.VideoLimitReached, "The media library holds the most videos it can."));
        }

        if (request.Size > limits.MaxBytes)
        {
            return (null, new VideoProblem(
                StatusCodes.Status413PayloadTooLarge, VideoProblem.TooLarge, "The video file is larger than the limit.",
                new Dictionary<string, object?> { ["maxBytes"] = limits.MaxBytes, ["maxSeconds"] = limits.MaxDuration.TotalSeconds }));
        }

        var key = MediaStorageKeys.BuildPrivateVideoKey(profileId, $"{Guid.CreateVersion7()}.mp4");
        var upload = new VideoUpload
        {
            OwnerProfileId = profileId,
            Target = target,
            TankId = tankId,
            CareEntryId = request.CareEntryId,
            OriginalFilename = request.FileName is { Length: > 0 } name ? Path.GetFileName(name)[..Math.Min(Path.GetFileName(name).Length, 200)] : null,
            ByteSize = request.Size.Value,
            ChunkSize = settings.ChunkSize,
            StorageKey = key,
            MultipartUploadId = await store.BeginMultipartUploadAsync(key, Mp4ContentType, cancellationToken),
            CreatedAt = now,
            ExpiresAt = now + settings.UploadLifetime,
        };
        try
        {
            context.VideoUploads.Add(upload);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await AbortQuietlyAsync(upload);
            throw;
        }

        return (ToDto(upload), null);
    }

    public async Task<(VideoUploadDto? Session, VideoProblem? Problem)> GetAsync(long profileId, long id, CancellationToken cancellationToken)
    {
        var upload = await context.VideoUploads.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.OwnerProfileId == profileId, cancellationToken);
        return upload is null ? (null, NotFound()) : (ToDto(upload), null);
    }

    /// <summary>
    /// Stores the next chunk. The body is read up to the expected length plus one byte, so the
    /// memory per request is bounded by the chunk size however much the client sends.
    /// </summary>
    public async Task<(long? Offset, VideoProblem? Problem)> AppendAsync(
        long profileId, long id, long? declaredOffset, long? declaredLength, Stream body, CancellationToken cancellationToken)
    {
        var upload = await context.VideoUploads.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.OwnerProfileId == profileId, cancellationToken);
        if (upload is null)
        {
            return (null, NotFound());
        }

        if (upload.ExpiresAt <= clock.GetUtcNow())
        {
            return (null, new VideoProblem(StatusCodes.Status410Gone, VideoProblem.UploadExpired, "The upload session has expired."));
        }

        if (declaredOffset is null)
        {
            return (null, new VideoProblem(StatusCodes.Status400BadRequest, VideoProblem.UploadInvalid, "The Upload-Offset header is required."));
        }

        if (declaredOffset != upload.Offset)
        {
            return (null, OffsetMismatch(upload.Offset));
        }

        if (upload.Offset >= upload.ByteSize)
        {
            return (null, new VideoProblem(StatusCodes.Status409Conflict, VideoProblem.UploadComplete, "All bytes are received; complete the upload."));
        }

        var expected = (int)Math.Min(upload.ChunkSize, upload.ByteSize - upload.Offset);
        if (declaredLength > expected)
        {
            return (null, ChunkTooLarge(expected));
        }

        var buffer = new byte[expected];
        var read = await body.ReadAtLeastAsync(buffer, expected, throwOnEndOfStream: false, cancellationToken);
        if (read < expected)
        {
            return (null, new VideoProblem(
                StatusCodes.Status400BadRequest, VideoProblem.ChunkIncomplete, "Only the last chunk may be shorter than the chunk size.",
                new Dictionary<string, object?> { ["expectedBytes"] = expected }));
        }

        if (await body.ReadAsync(new byte[1], cancellationToken) > 0)
        {
            return (null, ChunkTooLarge(expected));
        }

        string? container = null;
        if (upload.Offset == 0)
        {
            container = VideoContainerSniffer.Sniff(buffer) switch
            {
                VideoContainer.Mp4 => "mp4",
                VideoContainer.QuickTime => "quick_time",
                _ => null,
            };
            if (container is null)
            {
                await DiscardAsync(upload, cancellationToken);
                return (null, new VideoProblem(StatusCodes.Status400BadRequest, VideoProblem.FormatUnsupported, "The file is not an MP4 or QuickTime video."));
            }
        }

        var partNumber = (int)(upload.Offset / upload.ChunkSize) + 1;
        var etag = await store.UploadPartAsync(upload.StorageKey, upload.MultipartUploadId, partNumber, buffer, cancellationToken);
        var oldOffset = upload.Offset;
        var updated = await context.Database.ExecuteSqlAsync($"""
            UPDATE public.video_upload
            SET "offset" = "offset" + {expected}, part_e_tags = array_append(part_e_tags, {etag}), container = COALESCE(container, {container})
            WHERE id = {id} AND "offset" = {oldOffset}
            """, cancellationToken);
        if (updated == 0)
        {
            var current = await context.VideoUploads.AsNoTracking().Where(u => u.Id == id).Select(u => (long?)u.Offset).SingleOrDefaultAsync(cancellationToken);
            return (null, current is null ? NotFound() : OffsetMismatch(current.Value));
        }

        return (oldOffset + expected, null);
    }

    /// <summary>
    /// Discards an open session with the parts received so far.
    /// </summary>
    public async Task<VideoProblem?> CancelAsync(long profileId, long id, CancellationToken cancellationToken)
    {
        var upload = await context.VideoUploads.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.OwnerProfileId == profileId, cancellationToken);
        if (upload is null)
        {
            return NotFound();
        }

        await DiscardAsync(upload, cancellationToken);
        return null;
    }

    /// <summary>
    /// Assembles, probes and records a fully received upload. A video that is refused leaves
    /// neither its session nor its stored objects behind.
    /// </summary>
    public async Task<(VideoDto? Video, VideoProblem? Problem)> CompleteAsync(long profileId, long id, CancellationToken cancellationToken)
    {
        var upload = await context.VideoUploads.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.OwnerProfileId == profileId, cancellationToken);
        if (upload is null)
        {
            return (null, NotFound());
        }

        if (upload.Offset != upload.ByteSize)
        {
            return (null, new VideoProblem(StatusCodes.Status409Conflict, VideoProblem.UploadIncomplete, "Not all bytes are received yet."));
        }

        // Deleting the session claims it: of two concurrent completions only one deletes the row.
        if (await context.VideoUploads.Where(u => u.Id == id && u.Offset == u.ByteSize).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            return (null, NotFound());
        }

        var directory = Directory.CreateTempSubdirectory("video-upload-");
        try
        {
            var parts = upload.PartETags.Select((etag, index) => new MultipartPart(index + 1, etag)).ToList();
            await store.CompleteMultipartUploadAsync(upload.StorageKey, upload.MultipartUploadId, parts, cancellationToken);

            var path = Path.Combine(directory.FullName, "original.mp4");
            string sha256;
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
            {
                if (!await store.DownloadAsync(upload.StorageKey, file, upload.ByteSize, cancellationToken) || file.Length != upload.ByteSize)
                {
                    await deleter.DeleteAsync([upload.StorageKey]);
                    return (null, VideoProblem.OfProbe("video_unreadable"));
                }

                file.Position = 0;
                sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
            }

            var limits = await policy.ResolveAsync(profileId, cancellationToken);
            VideoProbe info;
            try
            {
                info = await probe.ProbeAsync(path, cancellationToken);
            }
            catch (VideoProbeException ex)
            {
                await deleter.DeleteAsync([upload.StorageKey]);
                return (null, VideoProblem.OfProbe(ex.Code));
            }
            catch (ProcessTimeoutException)
            {
                await deleter.DeleteAsync([upload.StorageKey]);
                return (null, VideoProblem.OfProbe(VideoProbeException.Unreadable));
            }

            if (info.Duration > limits.MaxDuration)
            {
                await deleter.DeleteAsync([upload.StorageKey]);
                return (null, new VideoProblem(
                    StatusCodes.Status422UnprocessableEntity, VideoProbeException.TooLong, "The video is longer than the limit.",
                    new Dictionary<string, object?> { ["maxSeconds"] = limits.MaxDuration.TotalSeconds }));
            }

            return await RecordAsync(upload, info, sha256, cancellationToken);
        }
        catch
        {
            await deleter.DeleteAsync([upload.StorageKey]);
            throw;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private async Task<(VideoDto? Video, VideoProblem? Problem)> RecordAsync(
        VideoUpload upload, VideoProbe info, string sha256, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await CareTanks.LockOwnAsync(context, upload.TankId, upload.OwnerProfileId, cancellationToken)
            || (upload.CareEntryId is { } entryId && !await context.CareEntries.AnyAsync(
                e => e.Id == entryId && e.TankId == upload.TankId && e.AuthorProfileId == upload.OwnerProfileId, cancellationToken)))
        {
            await deleter.DeleteAsync([upload.StorageKey]);
            return (null, new VideoProblem(StatusCodes.Status404NotFound, VideoProblem.TargetNotFound, "The tank or care entry is unavailable."));
        }

        if (upload.CareEntryId is { } careEntry
            && await context.MediaLinks.CountAsync(l => l.CareEntryId == careEntry && l.Role == MediaLinkRole.Video, cancellationToken) >= MediaLinkLimits.CareVideosPerEntry)
        {
            await deleter.DeleteAsync([upload.StorageKey]);
            return (null, new VideoProblem(StatusCodes.Status409Conflict, VideoProblem.CareVideoLimitReached, "The care entry holds the most videos it can."));
        }

        var item = new MediaItem
        {
            OwnerProfileId = upload.OwnerProfileId,
            Kind = MediaKind.Video,
            StorageKey = upload.StorageKey,
            OriginalFilename = upload.OriginalFilename,
            ContentType = upload.Container == "quick_time" ? "video/quicktime" : Mp4ContentType,
            ByteSize = upload.ByteSize,
            Width = info.DisplayWidth,
            Height = info.DisplayHeight,
            DurationMs = (int)info.Duration.TotalMilliseconds,
            ChecksumSha256 = sha256,
            CreatedAt = now,
            ReceivedAt = now,
            PlacedPublic = false,
            VideoJob = new VideoJob
            {
                Status = VideoJobStatus.Queued, RunAfter = now, CreatedAt = now,
            },
        };
        context.MediaItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);

        if (upload.CareEntryId is { } attachTo)
        {
            var nextCarePosition = (await context.MediaLinks.Where(l => l.CareEntryId == attachTo && l.Role == MediaLinkRole.Video)
                .MaxAsync(l => (int?)l.Position, cancellationToken) ?? -1) + 1;
            context.MediaLinks.Add(MediaLink.ForCareEntry(attachTo, item.Id, MediaLinkRole.Video, nextCarePosition));
        }
        else
        {
            var nextPosition = (await context.MediaLinks.Where(l => l.TankId == upload.TankId && l.Role == MediaLinkRole.Video)
                .MaxAsync(l => (int?)l.Position, cancellationToken) ?? -1) + 1;
            context.MediaLinks.Add(MediaLink.ForTank(upload.TankId, item.Id, MediaLinkRole.Video, nextPosition));
        }

        VideoEvents.Add(context, VideoEvents.UploadCompleted, item.Id, upload.OwnerProfileId, now,
            new { tankId = upload.TankId, careEntryId = upload.CareEntryId });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (new VideoDto(item.Id, VideoState.Processing, info.Duration.TotalSeconds, item.Width, item.Height, null, null, now, null), null);
    }

    private async Task DiscardAsync(VideoUpload upload, CancellationToken cancellationToken)
    {
        await context.VideoUploads.Where(u => u.Id == upload.Id).ExecuteDeleteAsync(cancellationToken);
        await AbortQuietlyAsync(upload);
    }

    private async Task AbortQuietlyAsync(VideoUpload upload)
    {
        try
        {
            await store.AbortMultipartUploadAsync(upload.StorageKey, upload.MultipartUploadId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not abort the multipart upload of {StorageKey}.", upload.StorageKey);
        }
    }

    private static bool TryParseTarget(string? text, out VideoUploadTarget target)
    {
        target = default;
        switch (text)
        {
            case "tank":
                target = VideoUploadTarget.Tank;
                return true;
            case "care_entry":
                target = VideoUploadTarget.CareEntry;
                return true;
            default:
                return false;
        }
    }

    private static VideoUploadDto ToDto(VideoUpload upload) => new(upload.Id, upload.Offset, upload.ByteSize, upload.ChunkSize, upload.ExpiresAt);

    private static VideoProblem NotFound() => new(StatusCodes.Status404NotFound, VideoProblem.UploadNotFound, "The upload session does not exist.");

    private static VideoProblem ChunkTooLarge(int expected) => new(
        StatusCodes.Status413PayloadTooLarge, VideoProblem.ChunkTooLarge, "The chunk is larger than expected.",
        new Dictionary<string, object?> { ["expectedBytes"] = expected });

    private static VideoProblem OffsetMismatch(long offset) => new(
        StatusCodes.Status409Conflict, VideoProblem.OffsetMismatch, "The chunk does not start at the stored offset.",
        new Dictionary<string, object?> { ["offset"] = offset });
}
