using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Pictures;

/// <summary>
/// Query logic behind the pictures list and detail endpoints: the public-visibility rule
/// (published, not deleted), the three legacy sort orders, and enrichment of each page with its
/// author, canonical slug, comment count and primary image without a per-row round trip.
/// </summary>
public sealed class PicturesQueryService(CichlidsDbContext context, IObjectStore objectStore)
{
    private static readonly IReadOnlyDictionary<string, string> NoVariants = new Dictionary<string, string>();
    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore);

    public async Task<PagedResponse<PictureListItemDto>> ListAsync(
        string? sort, PostTopic? topic, long? userId, string? species, int offset, int limit, CancellationToken cancellationToken)
    {
        var filtered = context.Posts.Where(p => p.State == PostState.Published && p.DeletedAt == null);

        filtered = topic is { } singleTopic
            ? filtered.Where(p => p.Topic == singleTopic)
            : filtered.Where(p => p.Topic == PostTopic.Cichlids || p.Topic == PostTopic.Tanks);

        if (userId is { } authorId)
        {
            filtered = filtered.Where(p => p.AuthorProfileId == authorId);
        }

        if (species is not null)
        {
            var speciesId = await ResolveSpeciesIdAsync(species, cancellationToken);
            filtered = filtered.Where(p => context.PostSpecies.Any(ps => ps.PostId == p.Id && ps.SpeciesId == speciesId));
        }

        var total = await filtered.CountAsync(cancellationToken);

        var sorted = sort switch
        {
            "views" => filtered.OrderByDescending(p => p.ViewCount).ThenByDescending(p => p.Id),
            "rating" => filtered
                .OrderByDescending(p =>
                    (double)p.RatingCount / (p.RatingCount + RatingScore.PriorWeight) * (p.RatingAverage ?? 0) +
                    (double)RatingScore.PriorWeight / (p.RatingCount + RatingScore.PriorWeight) * RatingScore.PriorMean)
                .ThenByDescending(p => p.Id),
            _ => filtered.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id),
        };

        var page = await sorted.Skip(offset).Take(limit)
            .Select(p => new PostProjection(
                p.Id, p.Title, p.Description, p.PublishedAt, p.ViewCount, p.RatingAverage, p.RatingCount, p.Topic, p.AuthorProfileId))
            .ToListAsync(cancellationToken);

        var items = await MapPageAsync(page, cancellationToken);
        return new PagedResponse<PictureListItemDto>(total, items);
    }

    /// <summary>
    /// Resolves a post by any of its slug aliases, or null when the slug is unknown or the post it
    /// names is not publicly visible. Does not affect the view counter, so it is safe to call from
    /// endpoints (such as comment listing) that need the post's id without counting it as a view.
    /// </summary>
    public async Task<long?> ResolveVisiblePostIdAsync(string slug, CancellationToken cancellationToken)
    {
        var postId = await context.SlugAliases
            .Where(s => s.Value == slug)
            .Select(s => (long?)s.PostId)
            .FirstOrDefaultAsync(cancellationToken);

        if (postId is null)
        {
            return null;
        }

        var isVisible = await context.Posts
            .AnyAsync(p => p.Id == postId && p.State == PostState.Published && p.DeletedAt == null, cancellationToken);

        return isVisible ? postId : null;
    }

    /// <summary>
    /// Resolves a post by any of its slug aliases and returns its detail, or null when the slug is
    /// unknown or the post it names is not publicly visible.
    /// </summary>
    public async Task<PictureDetailDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var postId = await ResolveVisiblePostIdAsync(slug, cancellationToken);
        if (postId is null)
        {
            return null;
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE post SET view_count = view_count + 1 WHERE id = {postId}", cancellationToken);

        return await GetDetailAsync(postId.Value, cancellationToken);
    }

    /// <summary>
    /// Returns the detail of an existing post by its id without counting a view.
    /// </summary>
    public async Task<PictureDetailDto> GetDetailAsync(long postId, CancellationToken cancellationToken)
    {
        var post = await context.Posts
            .Where(p => p.Id == postId)
            .Select(p => new PostProjection(
                p.Id, p.Title, p.Description, p.PublishedAt, p.ViewCount, p.RatingAverage, p.RatingCount, p.Topic, p.AuthorProfileId))
            .FirstAsync(cancellationToken);

        var mapped = (await MapPageAsync([post], cancellationToken))[0];

        return new PictureDetailDto(
            mapped.Id, mapped.Slug, mapped.Slug, mapped.Title, mapped.Description, mapped.PublishedAt,
            mapped.ViewCount, mapped.RatingAverage, mapped.RatingCount, mapped.CommentCount, mapped.Topic,
            mapped.Author, mapped.Image);
    }

    /// <summary>
    /// Resolves a species route segment (as used by the species filter) by slug first, falling
    /// back to a numeric id, the same resolution order as the species detail endpoint. Returns an
    /// id no species ever has when the segment matches neither, so an unknown filter value narrows
    /// the list to zero results instead of failing the request.
    /// </summary>
    private async Task<long> ResolveSpeciesIdAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var id = await context.Species
            .Where(s => s.Slug == idOrSlug)
            .Select(s => (long?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (id is null && long.TryParse(idOrSlug, out var numericId))
        {
            id = await context.Species
                .Where(s => s.Id == numericId)
                .Select(s => (long?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return id ?? -1;
    }

    private async Task<List<PictureListItemDto>> MapPageAsync(List<PostProjection> page, CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var postIds = page.Select(p => p.Id).ToList();
        var authorIds = page.Select(p => p.AuthorProfileId).Distinct().ToList();

        var authors = await AuthorBatchLoader.LoadAsync(context, authorIds, cancellationToken);

        var canonicalSlugs = await context.SlugAliases
            .Where(s => postIds.Contains(s.PostId) && s.IsCanonical)
            .ToDictionaryAsync(s => s.PostId, s => s.Value, cancellationToken);

        var commentCounts = await context.Comments
            .Where(c => c.PostId != null && postIds.Contains(c.PostId.Value) && c.DeletedAt == null)
            .GroupBy(c => c.PostId!.Value)
            .Select(g => new { PostId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PostId, x => x.Count, cancellationToken);

        var mediaRows = await context.PostMedia
            .Where(pm => postIds.Contains(pm.PostId))
            .Select(pm => new { pm.PostId, pm.MediaItemId, pm.Sort })
            .ToListAsync(cancellationToken);

        var firstMediaItemIdByPost = mediaRows
            .GroupBy(pm => pm.PostId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Sort).First().MediaItemId);

        var mediaItemIds = firstMediaItemIdByPost.Values.Distinct().ToList();

        var originalByMediaItem = await context.MediaItems
            .Where(m => mediaItemIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.StorageKey, cancellationToken);

        var variantsByMediaItem = (await context.MediaVariants
                .Where(v => mediaItemIds.Contains(v.MediaItemId))
                .Select(v => new { v.MediaItemId, v.Label, v.StorageKey })
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.MediaItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g.ToDictionary(x => x.Label, x => x.StorageKey));

        return page.Select(p => new PictureListItemDto(
            p.Id,
            canonicalSlugs.GetValueOrDefault(p.Id, string.Empty),
            p.Title,
            p.Description,
            p.PublishedAt,
            p.ViewCount,
            p.RatingAverage,
            p.RatingCount,
            commentCounts.GetValueOrDefault(p.Id, 0),
            p.Topic,
            authors[p.AuthorProfileId].ToDto(objectStore),
            firstMediaItemIdByPost.TryGetValue(p.Id, out var mediaItemId)
                ? _mediaUrlBuilder.Build(originalByMediaItem[mediaItemId], variantsByMediaItem.GetValueOrDefault(mediaItemId, NoVariants))
                : null))
            .ToList();
    }

    private sealed record PostProjection(
        long Id, string? Title, string? Description, DateTimeOffset? PublishedAt, long ViewCount,
        double? RatingAverage, int RatingCount, PostTopic Topic, long AuthorProfileId);
}
