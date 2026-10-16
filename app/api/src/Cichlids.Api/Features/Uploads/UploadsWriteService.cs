using System.Security.Cryptography;
using Cichlids.Api.Features.Media;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// Write logic behind the photo upload flow. An upload stores the original, with its personal
/// metadata removed, and its variants in the object store, each with the media object metadata,
/// and records the media item, its variants and a single-photo draft story in one transaction.
/// Objects are written before the commit, so when the database write fails the stored keys are
/// deleted (best effort, logged). Discarding removes the draft story and its links; the photos stay in the library.
/// Before an upload the owner's quota is checked: at most the configured number of open drafts
/// and of uploads within the last hour.
/// </summary>
public sealed class UploadsWriteService(
    CichlidsDbContext context,
    IObjectStore objectStore,
    IOptions<UploadLimitsOptions> limitOptions,
    TimeProvider timeProvider,
    StoredObjectDeleter deleter,
    MediaUrlSigner? signer = null)
{
    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore, signer);

    /// <summary>
    /// Checks the owner's upload quota and returns the limit that is reached, or null when the
    /// owner may upload. The open draft limit is checked before the hourly upload limit. The hourly
    /// count comes from the stored media items, so it holds across restarts and several instances.
    /// </summary>
    public async Task<UploadRefusal?> CheckQuotaAsync(long ownerProfileId, CancellationToken cancellationToken)
    {
        var limits = limitOptions.Value;

        var openDrafts = await context.Stories.CountAsync(
            p => p.AuthorProfileId == ownerProfileId && p.State == StoryState.Draft && p.DeletedAt == null,
            cancellationToken);
        if (openDrafts >= limits.MaxOpenDrafts)
        {
            return UploadRefusal.DraftLimitReached;
        }

        return await CheckHourlyQuotaAsync(ownerProfileId, cancellationToken);
    }

    /// <summary>
    /// Checks only the owner's hourly upload limit and returns RateLimited when it is reached, else
    /// null. A provisional profile has a limit of its own.
    /// </summary>
    public async Task<UploadRefusal?> CheckHourlyQuotaAsync(long ownerProfileId, CancellationToken cancellationToken)
    {
        var provisional = await context.Profiles.AsNoTracking()
            .AnyAsync(p => p.Id == ownerProfileId && p.Kind == ProfileKind.Provisional, cancellationToken);
        var limit = provisional ? limitOptions.Value.ProvisionalMaxUploadsPerHour : limitOptions.Value.MaxUploadsPerHour;
        var since = timeProvider.GetUtcNow().AddHours(-1);
        var recentUploads = await context.MediaItems.CountAsync(
            m => m.OwnerProfileId == ownerProfileId && m.CreatedAt > since, cancellationToken);
        return recentUploads >= limit ? UploadRefusal.RateLimited : null;
    }

    /// <summary>
    /// Stores an uploaded photo and creates the owner's draft story for it. Throws
    /// ImageProcessor.ImageTooLargeException when the photo declares more pixels than
    /// ImageProcessor.MaxPixels, a side longer than ImageProcessor.MaxSide or a long side more
    /// than ImageProcessor.MaxAspectRatio times its short side,
    /// ImageProcessor.UnsupportedImageFormatException for a format outside the image allowlist
    /// and ImageProcessor.BrokenImageException when the bytes cannot be decoded or resized, all
    /// before anything is stored.
    /// </summary>
    public async Task<DraftDto> CreateDraftAsync(
        Profile owner, byte[] bytes, UploadImageFormat format, string? originalFilename, CancellationToken cancellationToken)
    {
        var stored = await StoreImageAsync(owner.Id, bytes, format, originalFilename, isPrivate: true, cancellationToken, privateArea: "gallery");
        try
        {
            var now = stored.Item.CreatedAt;
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            context.MediaItems.Add(stored.Item);

            var story = new Story
            {
                AuthorProfileId = owner.Id,
                Topic = StoryTopic.Cichlids,
                State = StoryState.Draft,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Stories.Add(story);
            await context.SaveChangesAsync(cancellationToken);

            context.MediaLinks.Add(MediaLink.ForStory(story.Id, stored.Item.Id));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var generations = await context.VisibleMediaGenerationsAsync([stored.Item.Id], owner.Id, timeProvider.GetUtcNow(), cancellationToken);
            var image = _mediaUrlBuilder.Build(stored.Item.Variants, allowSigned: true, anonymousVisible: false,
                generations.TryGetValue(stored.Item.Id, out var generation) ? generation : null);
            return new DraftDto(story.Id, story.State, story.Topic, story.Title, story.Description, story.CreatedAt, image);
        }
        catch
        {
            await deleter.DeleteAsync(stored.WrittenKeys);
            throw;
        }
    }

    /// <summary>
    /// Processes a photo into its variants and writes the sanitized original and the variants to
    /// the object store, and reads the capture time from the uploaded bytes (EXIF DateTimeOriginal
    /// and OffsetTimeOriginal; nothing else of the EXIF block is read). A public photo lives under
    /// the public variant prefix; a private photo lives entirely under the private prefix, so none
    /// of its objects has a public URL; the area names the folder of the private original. Returns the
    /// media item with its variants, not yet saved, and the keys written so a failing caller can
    /// delete them. CapturedInApp records whether the camera of the app took the photo. Throws the
    /// image exceptions of CreateDraftAsync before anything is stored; a failing write deletes the
    /// objects already written.
    /// </summary>
    public async Task<StoredImage> StoreImageAsync(
        long ownerProfileId, byte[] bytes, UploadImageFormat format, string? originalFilename, bool isPrivate,
        CancellationToken cancellationToken, string privateArea = "care", bool capturedInApp = false)
    {
        var processed = ImageProcessor.Process(bytes);
        var captured = CaptureTimeReader.Read(bytes, timeProvider.GetUtcNow()).Time;
        var (width, height) = (processed.Width, processed.Height);
        var original = MediaMetadataSanitizer.Sanitize(bytes);
        var sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var originalSha256 = Convert.ToHexStringLower(SHA256.HashData(original));

        var fileName = $"{Guid.CreateVersion7()}.{format.Extension}";
        var originalKey = isPrivate
            ? MediaStorageKeys.BuildPrivateOriginalKey(privateArea, ownerProfileId, fileName)
            : $"originals/uploads/{ownerProfileId}/{fileName}";
        var variants = processed.Variants
            .Select(v => (v.Label, v.Width, Key: isPrivate
                ? MediaStorageKeys.BuildPrivateVariantKey(originalKey, v.Label)
                : MediaStorageKeys.BuildVariantKey(originalKey, v.Label), v.Bytes))
            .ToList();

        var writtenKeys = new List<string>();
        try
        {
            await objectStore.PutAsync(
                originalKey,
                new MemoryStream(original),
                format.ContentType,
                MediaObjectMetadata.ForOriginal(originalSha256, sourceSha256, new PhotoInfo(width, height)),
                cancellationToken);
            writtenKeys.Add(originalKey);

            foreach (var variant in variants)
            {
                await objectStore.PutAsync(
                    variant.Key,
                    new MemoryStream(variant.Bytes),
                    "image/jpeg",
                    MediaObjectMetadata.ForVariant(Convert.ToHexStringLower(SHA256.HashData(variant.Bytes)), sourceSha256),
                    cancellationToken);
                writtenKeys.Add(variant.Key);
            }
        }
        catch
        {
            await deleter.DeleteAsync(writtenKeys);
            throw;
        }

        var createdAt = DateTimeOffset.UtcNow;
        var item = new MediaItem
        {
            OwnerProfileId = ownerProfileId,
            Kind = MediaKind.Photo,
            StorageKey = originalKey,
            OriginalFilename = originalFilename,
            ContentType = format.ContentType,
            ByteSize = original.LongLength,
            Width = width,
            Height = height,
            ChecksumSha256 = originalSha256,
            CreatedAt = createdAt,
            TakenAtLocal = captured?.LocalTime,
            TakenAtOffsetMinutes = captured?.Offset is { } offset ? (short)offset.TotalMinutes : null,
            CaptureCheckedAt = createdAt,
            CapturedInApp = capturedInApp,
            PlacedPublic = !isPrivate,
            ReceivedAt = timeProvider.GetUtcNow(),
            Variants = variants
                .Select(v => new MediaVariant { Label = v.Label, Width = v.Width, StorageKey = v.Key, ByteSize = v.Bytes.LongLength })
                .ToList(),
        };
        return new StoredImage(item, writtenKeys);
    }

    /// <summary>
    /// Discards the author's draft: deletes the story and its media links. The photos stay in the media
    /// library of the author.
    /// </summary>
    public async Task<DraftActionOutcome> DiscardAsync(long storyId, long authorProfileId, CancellationToken cancellationToken)
    {
        return await RemoveDraftAsync(storyId, authorProfileId, changedBefore: null, cancellationToken);
    }

    /// <summary>
    /// Deletes a draft that was last changed before the given moment, whoever owns it, with its links
    /// like a discard. Returns false when there is no such draft any more, as
    /// when a concurrent cleanup deleted it first or it was published in the meantime.
    /// </summary>
    public async Task<bool> DeleteExpiredDraftAsync(long storyId, DateTimeOffset changedBefore, CancellationToken cancellationToken)
    {
        var outcome = await RemoveDraftAsync(storyId, authorProfileId: null, changedBefore, cancellationToken);
        return outcome == DraftActionOutcome.Succeeded;
    }

    private async Task<DraftActionOutcome> RemoveDraftAsync(
        long storyId, long? authorProfileId, DateTimeOffset? changedBefore, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // A story that was published has an address and stays; only a story that never was published is deleted.
        var candidates = context.Stories
            .Where(p => p.Id == storyId && p.DeletedAt == null && (authorProfileId == null || p.AuthorProfileId == authorProfileId));

        // The state condition in the delete itself serializes a removal against a concurrent
        // publish or removal of the same story on the row lock: whichever runs second sees the
        // committed outcome of the first and deletes nothing.
        var deleted = await candidates
            .Where(p => p.State == StoryState.Draft && !context.SlugAliases.Any(a => a.StoryId == p.Id) && (changedBefore == null || p.UpdatedAt < changedBefore))
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            var exists = await candidates.AnyAsync(cancellationToken);
            return exists ? DraftActionOutcome.NotDraft : DraftActionOutcome.NotFound;
        }

        await transaction.CommitAsync(cancellationToken);
        return DraftActionOutcome.Succeeded;
    }
}

/// <summary>
/// A processed photo: its media item with the variants (not yet saved) and the keys of the objects
/// written for it.
/// </summary>
public sealed record StoredImage(MediaItem Item, IReadOnlyList<string> WrittenKeys);
