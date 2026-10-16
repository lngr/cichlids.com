using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Stories;

/// <summary>
/// Reads the stories of their author for the editor: one story in any state with its items, its subjects and
/// its address, and the list of all stories of the author. Only the author sees a story here; a story of
/// another member, a deleted story and an archived story are not found.
/// </summary>
public sealed class StoryEditorQueryService(
    CichlidsDbContext context, StoriesQueryService stories, IObjectStore objectStore, MediaUrlSigner? signer = null)
{
    private readonly MediaUrlBuilder _urls = new(objectStore, signer);

    public async Task<StoryEditorDto?> GetAsync(long storyId, long authorProfileId, CancellationToken cancellationToken)
    {
        var story = await context.Stories.AsNoTracking()
            .Where(s => s.Id == storyId && s.AuthorProfileId == authorProfileId && s.DeletedAt == null && s.State != StoryState.Archived)
            .Select(s => new { s.State, s.UpdatedAt }).SingleOrDefaultAsync(cancellationToken);
        if (story is null)
        {
            return null;
        }

        var detail = await stories.GetDetailAsync(storyId, authorProfileId, cancellationToken);
        var slug = await context.SlugAliases.AsNoTracking().Where(a => a.StoryId == storyId && a.IsCanonical)
            .Select(a => a.Value).FirstOrDefaultAsync(cancellationToken);
        var shown = detail.Subjects.Select(s => s.Id).ToHashSet();
        var unavailable = (await context.StorySubjects.AsNoTracking().Where(s => s.StoryId == storyId).OrderBy(s => s.Id)
            .Select(s => s.Id).ToListAsync(cancellationToken)).Where(id => !shown.Contains(id)).ToList();
        var quickShare = await context.MediaItems.AsNoTracking().Where(m => m.QuickShareStoryId == storyId)
            .Select(m => (long?)m.Id).FirstOrDefaultAsync(cancellationToken);
        return new StoryEditorDto(
            storyId, story.State, slug, detail.Title, detail.Description, detail.Topic, detail.CoverItemId, detail.Cover,
            detail.Items, detail.Subjects, unavailable, quickShare, story.UpdatedAt);
    }

    /// <summary>
    /// The stories of the author, the most recently changed first. The cover is the chosen item or the first
    /// one, with signed URLs the author may use.
    /// </summary>
    public async Task<PagedResponse<MyStoryDto>> ListAsync(long authorProfileId, int offset, int limit, CancellationToken cancellationToken)
    {
        var mine = context.Stories.AsNoTracking()
            .Where(s => s.AuthorProfileId == authorProfileId && s.DeletedAt == null && s.State != StoryState.Archived);
        var total = await mine.CountAsync(cancellationToken);
        var page = await mine.OrderByDescending(s => s.UpdatedAt).ThenByDescending(s => s.Id).Skip(offset).Take(limit)
            .Select(s => new { s.Id, s.State, s.Title, s.Topic, s.UpdatedAt, s.CoverMediaLinkId }).ToListAsync(cancellationToken);
        var ids = page.Select(s => s.Id).ToList();

        var links = (await context.MediaLinks.StoryLinks().AsNoTracking().Where(l => ids.Contains(l.StoryId!.Value))
                .OrderBy(l => l.Position).ThenBy(l => l.Id).Select(l => new { StoryId = l.StoryId!.Value, l.Id, l.MediaItemId }).ToListAsync(cancellationToken))
            .GroupBy(l => l.StoryId).ToDictionary(g => g.Key, g => g.ToList());
        var coverItem = page.Where(s => links.ContainsKey(s.Id)).ToDictionary(s => s.Id,
            s => (links[s.Id].FirstOrDefault(l => l.Id == s.CoverMediaLinkId) ?? links[s.Id][0]).MediaItemId);
        var coverIds = coverItem.Values.Distinct().ToList();
        var photoIds = await context.MediaItems.AsNoTracking().Where(m => coverIds.Contains(m.Id) && m.Kind == MediaKind.Photo)
            .Select(m => m.Id).ToListAsync(cancellationToken);
        var variants = (await context.MediaVariants.AsNoTracking().Where(v => photoIds.Contains(v.MediaItemId)).ToListAsync(cancellationToken))
            .ToLookup(v => v.MediaItemId);
        var now = DateTimeOffset.UtcNow;
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(photoIds, now, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(photoIds, authorProfileId, now, cancellationToken);
        var slugs = (await context.SlugAliases.AsNoTracking().Where(a => ids.Contains(a.StoryId) && a.IsCanonical)
            .Select(a => new { a.StoryId, a.Value }).ToListAsync(cancellationToken)).ToDictionary(a => a.StoryId, a => a.Value);

        var items = page.Select(s =>
        {
            ImageUrlsDto? cover = coverItem.TryGetValue(s.Id, out var itemId) && photoIds.Contains(itemId)
                ? _urls.Build(variants[itemId], allowSigned: true, anonymousVisible.Contains(itemId), generations.TryGetValue(itemId, out var generation) ? generation : null)
                : null;
            return new MyStoryDto(s.Id, s.State, slugs.GetValueOrDefault(s.Id), s.Title, s.Topic, links.TryGetValue(s.Id, out var own) ? own.Count : 0, cover, s.UpdatedAt);
        }).ToList();
        return new PagedResponse<MyStoryDto>(total, items);
    }
}
