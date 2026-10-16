using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Stories;

/// <summary>
/// Query logic behind the story list and detail endpoints: the public-visibility rule (published, not
/// deleted), the three legacy sort orders, the cover of each story (the chosen item or the first one) and
/// the detail of a story with its items in order and the subjects the viewer may see. A subject that
/// points to a tank or an inhabitant is part of the response only when the viewer may see that tank.
/// </summary>
public sealed class StoriesQueryService(CichlidsDbContext context, IObjectStore objectStore, MediaUrlSigner? signer = null)
{
    private static readonly IReadOnlyDictionary<string, string> NoVariants = new Dictionary<string, string>();
    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore, signer);

    public async Task<PagedResponse<StoryListItemDto>> ListAsync(
        string? sort, StoryTopic? topic, long? userId, string? species, int offset, int limit, CancellationToken cancellationToken)
    {
        var filtered = context.Stories.Where(p => p.State == StoryState.Published && p.DeletedAt == null);

        filtered = topic is { } singleTopic
            ? filtered.Where(p => p.Topic == singleTopic)
            : filtered.Where(p => p.Topic == StoryTopic.Cichlids || p.Topic == StoryTopic.Tanks);

        if (userId is { } authorId)
        {
            filtered = filtered.Where(p => p.AuthorProfileId == authorId);
        }

        if (species is not null)
        {
            var speciesId = await ResolveSpeciesIdAsync(species, cancellationToken);
            filtered = filtered.Where(p => context.StorySubjects.Any(s => s.StoryId == p.Id && s.SpeciesId == speciesId));
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
            .Select(p => new StoryProjection(
                p.Id, p.Title, p.Description, p.PublishedAt, p.ViewCount, p.RatingAverage, p.RatingCount, p.Topic, p.AuthorProfileId,
                p.CoverMediaLinkId))
            .ToListAsync(cancellationToken);

        var items = await MapPageAsync(page, cancellationToken);
        return new PagedResponse<StoryListItemDto>(total, items);
    }

    /// <summary>
    /// Resolves a story by any of its slug aliases, or null when the slug is unknown or the story it
    /// names is not publicly visible. Does not affect the view counter, so it is safe to call from
    /// endpoints (such as comment listing) that need the story's id without counting it as a view.
    /// </summary>
    public async Task<long?> ResolveVisibleStoryIdAsync(string slug, CancellationToken cancellationToken)
    {
        var storyId = await context.SlugAliases
            .Where(s => s.Value == slug)
            .Select(s => (long?)s.StoryId)
            .FirstOrDefaultAsync(cancellationToken);

        if (storyId is null)
        {
            return null;
        }

        var isVisible = await context.Stories
            .AnyAsync(p => p.Id == storyId && p.State == StoryState.Published && p.DeletedAt == null, cancellationToken);

        return isVisible ? storyId : null;
    }

    /// <summary>
    /// Resolves a story by any of its slug aliases and returns its detail for the viewer, or null when
    /// the slug is unknown or the story it names is not publicly visible.
    /// </summary>
    public async Task<StoryDetailDto?> GetBySlugAsync(string slug, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var storyId = await ResolveVisibleStoryIdAsync(slug, cancellationToken);
        if (storyId is null)
        {
            return null;
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE story SET view_count = view_count + 1 WHERE id = {storyId}", cancellationToken);

        return await GetDetailAsync(storyId.Value, viewerProfileId, cancellationToken);
    }

    /// <summary>
    /// Returns the detail of an existing story by its id for the viewer without counting a view.
    /// </summary>
    public async Task<StoryDetailDto> GetDetailAsync(long storyId, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var story = await context.Stories
            .Where(p => p.Id == storyId)
            .Select(p => new StoryProjection(
                p.Id, p.Title, p.Description, p.PublishedAt, p.ViewCount, p.RatingAverage, p.RatingCount, p.Topic, p.AuthorProfileId,
                p.CoverMediaLinkId))
            .FirstAsync(cancellationToken);

        var mapped = (await MapPageAsync([story], cancellationToken))[0];
        var links = await LoadItemLinksAsync([storyId], cancellationToken);
        var itemLinks = links.GetValueOrDefault(storyId) ?? [];
        var mediaItemIds = itemLinks.Select(l => l.MediaItemId).Distinct().ToList();

        var now = DateTimeOffset.UtcNow;
        var mediaItems = await VideoQueryService.WithDetails(context.MediaItems.AsNoTracking().Where(m => mediaItemIds.Contains(m.Id)))
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(mediaItemIds, now, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(mediaItemIds, viewerProfileId, now, cancellationToken);
        long? Generation(long id) => generations.TryGetValue(id, out var generation) ? generation : null;

        var items = new List<StoryItemDto>();
        foreach (var link in itemLinks)
        {
            if (!mediaItems.TryGetValue(link.MediaItemId, out var item))
            {
                continue;
            }

            items.Add(item.Kind == MediaKind.Video
                ? new StoryItemDto(link.Id, item.Id, link.Position, link.Caption, item.Kind, null,
                    VideoQueryService.ToPublicDto(item, objectStore, signer, allowSigned: true, anonymousVisible.Contains(item.Id), Generation(item.Id)))
                : new StoryItemDto(link.Id, item.Id, link.Position, link.Caption, item.Kind,
                    _mediaUrlBuilder.Build(item.Variants, allowSigned: true, anonymousVisible.Contains(item.Id), Generation(item.Id)), null));
        }

        var coverItem = items.Count == 0 ? null
            : items.FirstOrDefault(i => i.Id == story.CoverMediaLinkId) ?? items[0];
        var subjects = await LoadVisibleSubjectsAsync(storyId, viewerProfileId, cancellationToken);

        return new StoryDetailDto(
            mapped.Id, mapped.Slug, mapped.Slug, mapped.Title, mapped.Description, mapped.PublishedAt,
            mapped.ViewCount, mapped.RatingAverage, mapped.RatingCount, mapped.CommentCount, mapped.Topic,
            mapped.Author, coverItem?.Image, coverItem?.Id, items, subjects);
    }

    /// <summary>
    /// The subjects of a story that the viewer may see, ordered like the sequence: positioned subjects
    /// by position, then the references in the order they were added. A tank subject needs a published
    /// tank whose profile level allows the viewer, an inhabitant subject the same of the tank it lives
    /// in. Species are public.
    /// </summary>
    private async Task<List<StorySubjectDto>> LoadVisibleSubjectsAsync(
        long storyId, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var rows = await context.StorySubjects.AsNoTracking().Where(s => s.StoryId == storyId)
            .OrderBy(s => s.Position == null).ThenBy(s => s.Position).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var visibleTanks = context.TanksVisibleTo(viewerProfileId)
            .Where(t => t.State == TankState.Published && t.DeletedAt == null);
        var tankIds = rows.Where(r => r.TankId != null).Select(r => r.TankId!.Value).ToList();
        var inhabitantIds = rows.Where(r => r.InhabitantId != null).Select(r => r.InhabitantId!.Value).ToList();
        var speciesIds = rows.Where(r => r.SpeciesId != null).Select(r => r.SpeciesId!.Value).ToList();

        var inhabitants = await context.Inhabitants.AsNoTracking()
            .Where(i => inhabitantIds.Contains(i.Id) && visibleTanks.Any(t => t.Id == i.TankId))
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        var allTankIds = tankIds.Concat(inhabitants.Values.Select(i => i.TankId)).Distinct().ToList();
        var tanks = await visibleTanks.AsNoTracking().Where(t => allTankIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Title, t.Category }).ToDictionaryAsync(t => t.Id, cancellationToken);

        var inhabitantSpeciesIds = inhabitants.Values.Where(i => i.SpeciesId != null).Select(i => i.SpeciesId!.Value);
        var allSpeciesIds = speciesIds.Concat(inhabitantSpeciesIds).Distinct().ToList();
        var species = await context.Species.AsNoTracking().Where(s => allSpeciesIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var tankImages = await LoadTankImagesAsync(tankIds, viewerProfileId, cancellationToken);

        var result = new List<StorySubjectDto>();
        foreach (var row in rows)
        {
            StorySubjectDto? dto = row.SubjectType switch
            {
                StorySubjectType.Tank when tanks.TryGetValue(row.TankId!.Value, out var tank) =>
                    new StorySubjectDto(row.Id, row.SubjectType, row.Position,
                        new StorySubjectTankDto(tank.Id, tank.Title, tank.Category, tankImages.GetValueOrDefault(tank.Id)), null, null),
                StorySubjectType.Inhabitant when inhabitants.TryGetValue(row.InhabitantId!.Value, out var inhabitant)
                    && tanks.TryGetValue(inhabitant.TankId, out var inhabitantTank) =>
                    new StorySubjectDto(row.Id, row.SubjectType, row.Position, null,
                        new StorySubjectInhabitantDto(inhabitant.Id, inhabitantTank.Id, inhabitantTank.Title,
                            inhabitant.SpeciesId is { } inhabitantSpeciesId && species.TryGetValue(inhabitantSpeciesId, out var s1)
                                ? new InhabitantSpeciesRefDto(s1.Id, s1.Slug, s1.Genus, s1.Name, s1.DisplayName) : null,
                            inhabitant.Count), null),
                StorySubjectType.Species when species.TryGetValue(row.SpeciesId!.Value, out var s2) =>
                    new StorySubjectDto(row.Id, row.SubjectType, row.Position, null, null,
                        new StorySubjectSpeciesDto(s2.Id, s2.Slug, s2.Genus, s2.Name, s2.DisplayName)),
                _ => null,
            };

            if (dto is not null)
            {
                result.Add(dto);
            }
        }

        return result;
    }

    /// <summary>
    /// The image of each tank card: the main image, otherwise the first showcase photo, as URLs the
    /// viewer may use.
    /// </summary>
    private async Task<Dictionary<long, ImageUrlsDto>> LoadTankImagesAsync(
        IReadOnlyCollection<long> tankIds, long? viewerProfileId, CancellationToken cancellationToken)
    {
        if (tankIds.Count == 0)
        {
            return [];
        }

        var rows = await context.MediaLinks.AsNoTracking()
            .Where(l => l.TankId != null && tankIds.Contains(l.TankId.Value)
                && (l.Role == MediaLinkRole.Main || l.Role == MediaLinkRole.Showcase))
            .Select(l => new { TankId = l.TankId!.Value, l.MediaItemId, l.Role, l.Position })
            .ToListAsync(cancellationToken);
        var imageItemByTank = rows.GroupBy(r => r.TankId).ToDictionary(g => g.Key,
            g => g.OrderBy(r => r.Role == MediaLinkRole.Main ? 0 : 1).ThenBy(r => r.Position).First().MediaItemId);
        var mediaItemIds = imageItemByTank.Values.Distinct().ToList();
        if (mediaItemIds.Count == 0)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        var variants = (await context.MediaVariants.AsNoTracking().Where(v => mediaItemIds.Contains(v.MediaItemId))
                .ToListAsync(cancellationToken)).ToLookup(v => v.MediaItemId);
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(mediaItemIds, now, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(mediaItemIds, viewerProfileId, now, cancellationToken);
        var images = mediaItemIds.ToDictionary(id => id, id => _mediaUrlBuilder.Build(
            variants[id], allowSigned: true, anonymousVisible.Contains(id), generations.TryGetValue(id, out var generation) ? generation : null));
        return imageItemByTank.ToDictionary(p => p.Key, p => images[p.Value]);
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

    /// <summary>
    /// The item links of the given stories, each story's links ordered by position.
    /// </summary>
    private async Task<Dictionary<long, List<ItemLink>>> LoadItemLinksAsync(
        IReadOnlyCollection<long> storyIds, CancellationToken cancellationToken)
    {
        var rows = await context.MediaLinks.StoryLinks().AsNoTracking()
            .Where(l => storyIds.Contains(l.StoryId!.Value))
            .OrderBy(l => l.Position).ThenBy(l => l.Id)
            .Select(l => new { StoryId = l.StoryId!.Value, Link = new ItemLink(l.Id, l.MediaItemId, l.Position, l.Caption) })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.StoryId).ToDictionary(g => g.Key, g => g.Select(r => r.Link).ToList());
    }

    private async Task<List<StoryListItemDto>> MapPageAsync(List<StoryProjection> page, CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var storyIds = page.Select(p => p.Id).ToList();
        var authorIds = page.Select(p => p.AuthorProfileId).Distinct().ToList();

        var authors = await AuthorBatchLoader.LoadAsync(context, authorIds, cancellationToken);

        var canonicalSlugs = await context.SlugAliases
            .Where(s => storyIds.Contains(s.StoryId) && s.IsCanonical)
            .ToDictionaryAsync(s => s.StoryId, s => s.Value, cancellationToken);

        var commentCounts = await context.Comments
            .Where(c => c.StoryId != null && storyIds.Contains(c.StoryId.Value) && c.DeletedAt == null)
            .GroupBy(c => c.StoryId!.Value)
            .Select(g => new { StoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StoryId, x => x.Count, cancellationToken);

        var links = await LoadItemLinksAsync(storyIds, cancellationToken);
        var coverMediaItemIdByStory = page
            .Where(p => links.ContainsKey(p.Id))
            .ToDictionary(p => p.Id, p =>
            {
                var itemLinks = links[p.Id];
                return (itemLinks.FirstOrDefault(l => l.Id == p.CoverMediaLinkId) ?? itemLinks[0]).MediaItemId;
            });

        var mediaItemIds = coverMediaItemIdByStory.Values.Distinct().ToList();

        var variantsByMediaItem = (await context.MediaVariants
                .Where(v => mediaItemIds.Contains(v.MediaItemId))
                .Select(v => new { v.MediaItemId, v.Label, v.StorageKey })
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.MediaItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g.ToDictionary(x => x.Label, x => x.StorageKey));

        return page.Select(p => new StoryListItemDto(
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
            coverMediaItemIdByStory.TryGetValue(p.Id, out var mediaItemId)
                ? _mediaUrlBuilder.Build(variantsByMediaItem.GetValueOrDefault(mediaItemId, NoVariants))
                : null,
            links.TryGetValue(p.Id, out var itemLinks) ? itemLinks.Count : 0))
            .ToList();
    }

    private sealed record StoryProjection(
        long Id, string? Title, string? Description, DateTimeOffset? PublishedAt, long ViewCount,
        double? RatingAverage, int RatingCount, StoryTopic Topic, long AuthorProfileId, long? CoverMediaLinkId);

    private sealed record ItemLink(long Id, long MediaItemId, int Position, string? Caption);
}
