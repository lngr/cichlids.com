using System.Security.Cryptography;
using Cichlids.Api.Features.Media;
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
/// and records the media item, its variants and a single-photo draft post in one transaction.
/// Objects are written before the commit, so when the database write fails the stored keys are
/// deleted (best effort, logged). Discarding removes a draft's rows and then its stored objects.
/// Before an upload the owner's quota is checked: at most the configured number of open drafts
/// and of uploads within the last hour.
/// </summary>
public sealed class UploadsWriteService(
    CichlidsDbContext context,
    IObjectStore objectStore,
    IOptions<UploadLimitsOptions> limitOptions,
    TimeProvider timeProvider,
    ILogger<UploadsWriteService> logger)
{
    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore);

    /// <summary>
    /// Checks the owner's upload quota and returns the limit that is reached, or null when the
    /// owner may upload. The open draft limit is checked before the hourly upload limit. The hourly
    /// count comes from the stored media items, so it holds across restarts and several instances.
    /// </summary>
    public async Task<UploadRefusal?> CheckQuotaAsync(long ownerProfileId, CancellationToken cancellationToken)
    {
        var limits = limitOptions.Value;

        var openDrafts = await context.Posts.CountAsync(
            p => p.AuthorProfileId == ownerProfileId && p.State == PostState.Draft && p.DeletedAt == null,
            cancellationToken);
        if (openDrafts >= limits.MaxOpenDrafts)
        {
            return UploadRefusal.DraftLimitReached;
        }

        var since = timeProvider.GetUtcNow().AddHours(-1);
        var recentUploads = await context.MediaItems.CountAsync(
            m => m.OwnerProfileId == ownerProfileId && m.CreatedAt > since, cancellationToken);
        return recentUploads >= limits.MaxUploadsPerHour ? UploadRefusal.RateLimited : null;
    }

    /// <summary>
    /// Stores an uploaded photo and creates the owner's draft post for it. Throws
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
        var processed = ImageProcessor.Process(bytes);
        var (width, height) = (processed.Width, processed.Height);
        var original = MediaMetadataSanitizer.Sanitize(bytes);
        var sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var originalSha256 = Convert.ToHexStringLower(SHA256.HashData(original));

        var originalKey = $"originals/uploads/{owner.Id}/{Guid.CreateVersion7()}.{format.Extension}";
        var variants = processed.Variants
            .Select(v => (v.Label, v.Width, Key: MediaStorageKeys.BuildVariantKey(originalKey, v.Label), v.Bytes))
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

            var now = DateTimeOffset.UtcNow;
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var mediaItem = new MediaItem
            {
                OwnerProfileId = owner.Id,
                Kind = MediaKind.Photo,
                StorageKey = originalKey,
                OriginalFilename = originalFilename,
                ContentType = format.ContentType,
                ByteSize = original.LongLength,
                Width = width,
                Height = height,
                ChecksumSha256 = originalSha256,
                CreatedAt = now,
                Variants = variants
                    .Select(v => new MediaVariant { Label = v.Label, Width = v.Width, StorageKey = v.Key, ByteSize = v.Bytes.LongLength })
                    .ToList(),
            };
            context.MediaItems.Add(mediaItem);

            var post = new Post
            {
                AuthorProfileId = owner.Id,
                Kind = PostKind.Single,
                Topic = PostTopic.Cichlids,
                State = PostState.Draft,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Posts.Add(post);
            await context.SaveChangesAsync(cancellationToken);

            context.PostMedia.Add(new PostMedia { PostId = post.Id, MediaItemId = mediaItem.Id, Sort = 0 });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var image = _mediaUrlBuilder.Build(variants.ToDictionary(v => v.Label, v => v.Key));
            return new DraftDto(post.Id, post.State, post.Topic, post.Title, post.Description, post.CreatedAt, image);
        }
        catch
        {
            await DeleteObjectsAsync(writtenKeys);
            throw;
        }
    }

    /// <summary>
    /// Discards the author's draft: deletes the post, its post_media rows, its media items and
    /// their variants, then the stored original and variant objects. Object deletion runs after
    /// the commit, so a failure there leaves unreferenced objects behind (logged) and never rows
    /// that point at missing objects.
    /// </summary>
    public async Task<DraftActionOutcome> DiscardAsync(long postId, long authorProfileId, CancellationToken cancellationToken)
    {
        return await RemoveDraftAsync(postId, authorProfileId, changedBefore: null, cancellationToken);
    }

    /// <summary>
    /// Deletes a draft that was last changed before the given moment, whoever owns it, with its rows
    /// and stored objects like a discard. Returns false when there is no such draft any more, as
    /// when a concurrent cleanup deleted it first or it was published in the meantime.
    /// </summary>
    public async Task<bool> DeleteExpiredDraftAsync(long postId, DateTimeOffset changedBefore, CancellationToken cancellationToken)
    {
        var outcome = await RemoveDraftAsync(postId, authorProfileId: null, changedBefore, cancellationToken);
        return outcome == DraftActionOutcome.Succeeded;
    }

    private async Task<DraftActionOutcome> RemoveDraftAsync(
        long postId, long? authorProfileId, DateTimeOffset? changedBefore, CancellationToken cancellationToken)
    {
        List<string> storedKeys;

        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            var mediaItemIds = await context.PostMedia
                .Where(pm => pm.PostId == postId)
                .Select(pm => pm.MediaItemId)
                .ToListAsync(cancellationToken);

            storedKeys = await context.MediaItems
                .Where(m => mediaItemIds.Contains(m.Id))
                .Select(m => m.StorageKey)
                .Concat(context.MediaVariants.Where(v => mediaItemIds.Contains(v.MediaItemId)).Select(v => v.StorageKey))
                .ToListAsync(cancellationToken);

            var candidates = context.Posts
                .Where(p => p.Id == postId && p.DeletedAt == null && (authorProfileId == null || p.AuthorProfileId == authorProfileId));

            // The state condition in the delete itself serializes a removal against a concurrent
            // publish or removal of the same post on the row lock: whichever runs second sees the
            // committed outcome of the first and deletes nothing.
            var deleted = await candidates
                .Where(p => p.State == PostState.Draft && (changedBefore == null || p.UpdatedAt < changedBefore))
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted == 0)
            {
                var exists = await candidates.AnyAsync(cancellationToken);
                return exists ? DraftActionOutcome.NotDraft : DraftActionOutcome.NotFound;
            }

            await context.MediaItems.Where(m => mediaItemIds.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await DeleteObjectsAsync(storedKeys);
        return DraftActionOutcome.Succeeded;
    }

    // Runs without the request's cancellation token: cleanup that stops halfway because the
    // client went away would leave exactly the orphans it exists to prevent.
    private async Task DeleteObjectsAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                await objectStore.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete stored object {StorageKey}.", key);
            }
        }
    }
}
