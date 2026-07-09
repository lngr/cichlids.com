using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Community;

/// <summary>
/// Query logic behind the community archive endpoints (ADR-0021): per-category counts, the
/// thread list with its opening author, and a thread's posts with each post's author resolved to
/// either a real member's profile or just a display name, never an e-mail address.
/// </summary>
public sealed class CommunityQueryService(CichlidsDbContext context, IObjectStore objectStore)
{
    private static readonly IReadOnlyDictionary<string, string> NoVariants = new Dictionary<string, string>();
    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore);

    public async Task<IReadOnlyList<CommunityCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var counts = await context.DiscussionThreads
            .GroupBy(t => t.Category)
            .Select(g => new
            {
                Category = g.Key,
                ThreadCount = g.Count(),
                PostCount = g.Sum(t => t.PostCount),
                LastPostAt = g.Max(t => t.LastPostAt),
            })
            .ToDictionaryAsync(x => x.Category, cancellationToken);

        return Enum.GetValues<DiscussionCategory>()
            .Select(category => counts.TryGetValue(category, out var row)
                ? new CommunityCategoryDto(category, row.ThreadCount, row.PostCount, row.LastPostAt)
                : new CommunityCategoryDto(category, 0, 0, null))
            .ToList();
    }

    public async Task<PagedResponse<CommunityThreadListItemDto>> ListThreadsAsync(
        DiscussionCategory? category, string? query, int offset, int limit, CancellationToken cancellationToken)
    {
        var filtered = context.DiscussionThreads.AsQueryable();

        if (category is { } singleCategory)
        {
            filtered = filtered.Where(t => t.Category == singleCategory);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query}%";
            filtered = filtered.Where(t => EF.Functions.ILike(t.Title, pattern));
        }

        var total = await filtered.CountAsync(cancellationToken);

        var page = await filtered
            .OrderByDescending(t => t.LastPostAt).ThenByDescending(t => t.Id)
            .Skip(offset).Take(limit)
            .Select(t => new ThreadProjection(t.Id, t.Title, t.Category, t.CreatedAt, t.LastPostAt, t.PostCount))
            .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return new PagedResponse<CommunityThreadListItemDto>(total, []);
        }

        var threadIds = page.Select(t => t.Id).ToList();

        // The thread's opening post always carries sort 0 (ForumMigrationStep numbers every
        // thread's posts from zero), so this is the one post per thread that names its starter.
        var openingPosts = await context.DiscussionPosts
            .Where(p => threadIds.Contains(p.ThreadId) && p.Sort == 0)
            .Select(p => new { p.ThreadId, p.AuthorProfileId, p.PosterName })
            .ToDictionaryAsync(p => p.ThreadId, cancellationToken);

        var authorProfileIds = openingPosts.Values
            .Where(p => p.AuthorProfileId != null)
            .Select(p => p.AuthorProfileId!.Value)
            .Distinct()
            .ToList();

        var authorRows = await LoadAuthorRowsAsync(authorProfileIds, cancellationToken);

        var items = page.Select(t =>
        {
            var opening = openingPosts.GetValueOrDefault(t.Id);
            var resolved = ResolveAuthor(opening?.AuthorProfileId, opening?.PosterName, authorRows);
            return new CommunityThreadListItemDto(t.Id, t.Title, t.Category, t.CreatedAt, t.LastPostAt, t.PostCount, resolved.ToStartedBy());
        }).ToList();

        return new PagedResponse<CommunityThreadListItemDto>(total, items);
    }

    public async Task<CommunityThreadDetailDto?> GetThreadDetailAsync(long id, CancellationToken cancellationToken)
    {
        var thread = await context.DiscussionThreads
            .Where(t => t.Id == id)
            .Select(t => new ThreadDetailProjection(t.Id, t.Title, t.Category, t.State, t.CreatedAt, t.LastPostAt, t.PostCount))
            .FirstOrDefaultAsync(cancellationToken);

        if (thread is null)
        {
            return null;
        }

        var posts = await context.DiscussionPosts
            .Where(p => p.ThreadId == id)
            .OrderBy(p => p.Sort).ThenBy(p => p.Id)
            .Select(p => new PostProjection(p.Id, p.Body, p.CreatedAt, p.AuthorProfileId, p.PosterName))
            .ToListAsync(cancellationToken);

        var authorProfileIds = posts.Where(p => p.AuthorProfileId != null).Select(p => p.AuthorProfileId!.Value).Distinct().ToList();
        var authorRows = await LoadAuthorRowsAsync(authorProfileIds, cancellationToken);

        var attachmentsByPost = await LoadAttachmentsAsync(posts.Select(p => p.Id).ToList(), cancellationToken);

        var postDtos = posts.Select(p =>
        {
            var resolved = ResolveAuthor(p.AuthorProfileId, p.PosterName, authorRows);
            return new CommunityPostDto(
                p.Id, p.Body, p.CreatedAt, resolved.ToPostAuthor(),
                attachmentsByPost.GetValueOrDefault(p.Id, []));
        }).ToList();

        return new CommunityThreadDetailDto(
            thread.Id, thread.Title, thread.Category, thread.State, thread.CreatedAt, thread.LastPostAt, thread.PostCount, postDtos);
    }

    /// <summary>
    /// Loads the fields needed to resolve a post's author: unlike <see cref="AuthorBatchLoader"/>,
    /// this also needs <see cref="Cichlids.Domain.Enums.ProfileKind"/> to tell a real member from
    /// an unlisted placeholder profile.
    /// </summary>
    private Task<Dictionary<long, ProfileAuthorRow>> LoadAuthorRowsAsync(
        IReadOnlyCollection<long> profileIds, CancellationToken cancellationToken) =>
        context.Profiles
            .Where(p => profileIds.Contains(p.Id))
            .Select(p => new ProfileAuthorRow(
                p.Id,
                p.Username,
                p.DisplayName,
                p.Kind,
                p.AvatarMediaId != null
                    ? context.MediaVariants
                        .Where(v => v.MediaItemId == p.AvatarMediaId && v.Label == "thumb")
                        .Select(v => v.StorageKey)
                        .FirstOrDefault()
                    : null,
                p.ExternalAvatarUrl))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

    /// <summary>
    /// Resolves the author of one post or thread-opening post to either a real member's public
    /// identity or just the name to display, per ADR-0021: a forum author matched by e-mail to a
    /// migrated profile is a real member; every other registered forum author became an unlisted
    /// placeholder profile, and a guest post has no profile at all.
    /// </summary>
    private ResolvedAuthor ResolveAuthor(
        long? authorProfileId, string? posterName, IReadOnlyDictionary<long, ProfileAuthorRow> authorRows)
    {
        if (authorProfileId is { } id && authorRows.TryGetValue(id, out var row))
        {
            var displayName = row.DisplayName ?? row.Username;

            if (row.Kind == ProfileKind.Member)
            {
                var avatarUrl = row.AvatarThumbStorageKey is not null
                    ? objectStore.GetPublicUrl(row.AvatarThumbStorageKey)
                    : row.ExternalAvatarUrl;
                return new ResolvedAuthor(row.Id, row.Username, displayName, avatarUrl, IsMember: true);
            }

            return new ResolvedAuthor(null, null, displayName, null, IsMember: false);
        }

        return new ResolvedAuthor(null, null, posterName ?? "Guest", null, IsMember: false);
    }

    private async Task<Dictionary<long, List<CommunityAttachmentDto>>> LoadAttachmentsAsync(
        List<long> postIds, CancellationToken cancellationToken)
    {
        var mediaRows = await context.DiscussionPostMedia
            .Where(m => postIds.Contains(m.DiscussionPostId))
            .OrderBy(m => m.Sort)
            .Select(m => new { m.DiscussionPostId, m.MediaItemId })
            .ToListAsync(cancellationToken);

        if (mediaRows.Count == 0)
        {
            return [];
        }

        var mediaItemIds = mediaRows.Select(m => m.MediaItemId).Distinct().ToList();

        var originalByMediaItem = await context.MediaItems
            .Where(m => mediaItemIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.StorageKey, cancellationToken);

        var variantsByMediaItem = (await context.MediaVariants
                .Where(v => mediaItemIds.Contains(v.MediaItemId))
                .Select(v => new { v.MediaItemId, v.Label, v.StorageKey })
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.MediaItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g.ToDictionary(x => x.Label, x => x.StorageKey));

        return mediaRows
            .GroupBy(m => m.DiscussionPostId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(m => new CommunityAttachmentDto(
                    m.MediaItemId,
                    _mediaUrlBuilder.Build(originalByMediaItem[m.MediaItemId], variantsByMediaItem.GetValueOrDefault(m.MediaItemId, NoVariants))))
                    .ToList());
    }

    private sealed record ThreadProjection(
        long Id, string Title, DiscussionCategory Category, DateTimeOffset CreatedAt, DateTimeOffset? LastPostAt, int PostCount);

    private sealed record ThreadDetailProjection(
        long Id, string Title, DiscussionCategory Category, DiscussionThreadState State,
        DateTimeOffset CreatedAt, DateTimeOffset? LastPostAt, int PostCount);

    private sealed record PostProjection(long Id, string Body, DateTimeOffset CreatedAt, long? AuthorProfileId, string? PosterName);

    private sealed record ProfileAuthorRow(
        long Id, string Username, string? DisplayName, ProfileKind Kind, string? AvatarThumbStorageKey, string? ExternalAvatarUrl);

    /// <summary>
    /// A post's author resolved once, then rendered into whichever of the two shapes the caller
    /// needs (<see cref="ThreadStartedByDto"/> or <see cref="CommunityPostAuthorDto"/>).
    /// </summary>
    private readonly record struct ResolvedAuthor(long? Id, string? Username, string DisplayName, string? AvatarUrl, bool IsMember)
    {
        public ThreadStartedByDto ToStartedBy() => IsMember
            ? new ThreadStartedByDto(new AuthorDto(Id!.Value, Username!, DisplayName, AvatarUrl), null)
            : new ThreadStartedByDto(null, DisplayName);

        public CommunityPostAuthorDto ToPostAuthor() =>
            new(IsMember ? Id : null, IsMember ? Username : null, DisplayName, IsMember ? AvatarUrl : null);
    }
}
