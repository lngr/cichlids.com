using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Stories;

/// <summary>
/// The problem codes of the story editor.
/// </summary>
public static class StoryEditorCodes
{
    public const string StoryNotFound = "story_not_found";
    public const string MediaNotFound = "media_not_found";
    public const string MediaNotReady = "media_not_ready";
    public const string MediaAlreadyShared = "media_already_shared";
    public const string ItemExists = "story_item_exists";
    public const string ItemLimit = "story_item_limit";
    public const string ItemNotFound = "story_item_not_found";
    public const string SubjectExists = "story_subject_exists";
    public const string SubjectLimit = "story_subject_limit";
    public const string SubjectNotFound = "story_subject_not_found";
    public const string OrderInvalid = "story_order_invalid";
    public const string CoverInvalid = "story_cover_invalid";
    public const string NeedsItem = "story_needs_item";
    public const string NotPublished = "story_not_published";
    public const string TextInvalid = "story_text_invalid";
    public const string CaptionInvalid = "story_caption_invalid";
}

/// <summary>
/// The outcome of an edit of a story: the problem code when it was refused, otherwise the story as the editor
/// shows it afterwards.
/// </summary>
public sealed record StoryEditResult(string? Error, StoryEditorDto? Editor)
{
    public static StoryEditResult Fail(string error) => new(error, null);
}

/// <summary>
/// Writes the stories of their author: creates drafts, edits text, items, captions, cover, subjects and the
/// order, withdraws and deletes stories and shares a file to the gallery with one step. Every edit runs in one
/// transaction under the lock of the story row. Items and positioned subjects share one dense position
/// sequence per story, and every edit that changes the members of the sequence renumbers it. Only the author
/// edits a story and only own files become items; everything else answers like a missing story or file.
/// </summary>
public sealed class StoryEditorService(
    CichlidsDbContext context,
    StoryEditorQueryService query,
    StoriesQueryService stories,
    StoryPublishService publication,
    MediaPlacementTrigger placement,
    IOptions<UploadLimitsOptions> uploadLimits,
    TimeProvider clock)
{
    public const int MaxItems = 30;
    public const int MaxSubjects = 20;
    public const int MaxCaptionLength = 500;

    // Renumbering moves every position above the range of the final positions first, so the unique indexes
    // on the positions never see two rows with the same value.
    private const int RenumberShift = 1_000_000;

    /// <summary>
    /// Creates an empty draft of the author with the given text.
    /// </summary>
    public async Task<StoryEditResult> CreateAsync(long authorProfileId, StoryTextRequest request, CancellationToken cancellationToken)
    {
        if (!UploadsEndpoints.TryValidateText(request.Title, request.Description, request.Topic, titleRequired: false,
                out var title, out var description, out var topic, out _))
        {
            return StoryEditResult.Fail(StoryEditorCodes.TextInvalid);
        }

        var openDrafts = await context.Stories.CountAsync(
            s => s.AuthorProfileId == authorProfileId && s.State == StoryState.Draft && s.DeletedAt == null, cancellationToken);
        if (openDrafts >= uploadLimits.Value.MaxOpenDrafts)
        {
            return StoryEditResult.Fail(UploadsEndpoints.DraftLimitReachedCode);
        }

        var now = clock.GetUtcNow();
        var story = new Story
        {
            AuthorProfileId = authorProfileId, Title = title.Length == 0 ? null : title, Description = description, Topic = topic,
            State = StoryState.Draft, CreatedAt = now, UpdatedAt = now,
        };
        context.Stories.Add(story);
        await context.SaveChangesAsync(cancellationToken);
        return new StoryEditResult(null, await query.GetAsync(story.Id, authorProfileId, cancellationToken));
    }

    /// <summary>
    /// Replaces title, text and topic. A published story keeps a title.
    /// </summary>
    public Task<StoryEditResult> UpdateTextAsync(long storyId, long authorProfileId, StoryTextRequest request, CancellationToken cancellationToken) =>
        RunAsync(storyId, authorProfileId, cancellationToken, async (state, _, _) =>
        {
            if (!UploadsEndpoints.TryValidateText(request.Title, request.Description, request.Topic, titleRequired: state == StoryState.Published,
                    out var title, out var description, out var topic, out _))
            {
                return StoryEditorCodes.TextInvalid;
            }

            await context.Stories.Where(s => s.Id == storyId).ExecuteUpdateAsync(
                set => set.SetProperty(s => s.Title, title.Length == 0 ? null : title).SetProperty(s => s.Description, description)
                    .SetProperty(s => s.Topic, topic), cancellationToken);
            return null;
        });

    /// <summary>
    /// Adds a file of the author as the last entry of the sequence. A file can be an item of any number of
    /// stories, and of each story once.
    /// </summary>
    public Task<StoryEditResult> AddItemAsync(long storyId, long authorProfileId, AddStoryItemRequest request, CancellationToken cancellationToken) =>
        RunAsync(storyId, authorProfileId, cancellationToken, async (state, now, placeIds) =>
        {
            if (!TryNormalizeCaption(request.Caption, out var caption))
            {
                return StoryEditorCodes.CaptionInvalid;
            }

            if (await CheckOwnFileAsync(request.MediaItemId, authorProfileId, cancellationToken) is { } fileError)
            {
                return fileError;
            }

            if (await context.MediaLinks.StoryLinks().AnyAsync(l => l.StoryId == storyId && l.MediaItemId == request.MediaItemId, cancellationToken))
            {
                return StoryEditorCodes.ItemExists;
            }

            if (await context.MediaLinks.StoryLinks().CountAsync(l => l.StoryId == storyId, cancellationToken) >= MaxItems)
            {
                return StoryEditorCodes.ItemLimit;
            }

            var link = MediaLink.ForStory(storyId, request.MediaItemId, await NextPositionAsync(storyId, cancellationToken));
            link.Caption = caption;
            link.CreatedAt = now;
            context.MediaLinks.Add(link);
            if (state == StoryState.Published)
            {
                placeIds.Add(request.MediaItemId);
            }

            return null;
        });

    /// <summary>
    /// Sets or removes the caption of an item of the story.
    /// </summary>
    public Task<StoryEditResult> SetCaptionAsync(long storyId, long authorProfileId, long itemId, string? rawCaption, CancellationToken cancellationToken) =>
        RunAsync(storyId, authorProfileId, cancellationToken, async (_, _, _) =>
        {
            if (!TryNormalizeCaption(rawCaption, out var caption))
            {
                return StoryEditorCodes.CaptionInvalid;
            }

            var changed = await context.MediaLinks.StoryLinks().Where(l => l.StoryId == storyId && l.Id == itemId)
                .ExecuteUpdateAsync(set => set.SetProperty(l => l.Caption, caption), cancellationToken);
            return changed == 0 ? StoryEditorCodes.ItemNotFound : null;
        });

    /// <summary>
    /// Removes an item from the story. Only the link goes; the file stays in the library. A published story
    /// keeps at least one item.
    /// </summary>
    public Task<StoryEditResult> RemoveItemAsync(long storyId, long authorProfileId, long itemId, CancellationToken cancellationToken) =>
        RunAsync(storyId, authorProfileId, cancellationToken, async (state, _, placeIds) =>
        {
            var link = await context.MediaLinks.StoryLinks().AsNoTracking().Where(l => l.StoryId == storyId && l.Id == itemId)
                .Select(l => new { l.MediaItemId }).SingleOrDefaultAsync(cancellationToken);
            if (link is null)
            {
                return StoryEditorCodes.ItemNotFound;
            }

            if (state == StoryState.Published && await context.MediaLinks.StoryLinks().CountAsync(l => l.StoryId == storyId, cancellationToken) <= 1)
            {
                return StoryEditorCodes.NeedsItem;
            }

            await context.MediaLinks.Where(l => l.Id == itemId).ExecuteDeleteAsync(cancellationToken);
            await context.MediaItems.Where(m => m.Id == link.MediaItemId && m.QuickShareStoryId == storyId)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.QuickShareStoryId, (long?)null), cancellationToken);
            await RenumberAsync(storyId, await CurrentOrderAsync(storyId, cancellationToken), cancellationToken);
            if (state == StoryState.Published)
            {
                placeIds.Add(link.MediaItemId);
            }

            return null;
        });

    /// <summary>
    /// Sets the sequence of items and positioned subjects. The request lists every one of them exactly once.
    /// </summary>
    public Task<StoryEditResult> ReorderAsync(long storyId, long authorProfileId, StoryOrderRequest request, CancellationToken cancellationToken) =>
        RunAsync(storyId, authorProfileId, cancellationToken, async (_, _, _) =>
        {
            var wanted = request.Entries ?? [];
            var current = await CurrentOrderAsync(storyId, cancellationToken);
            if (wanted.Count != current.Count || wanted.Distinct().Count() != wanted.Count || !current.ToHashSet().SetEquals(wanted))
            {
                return StoryEditorCodes.OrderInvalid;
            }

            await RenumberAsync(storyId, wanted, cancellationToken);
            return null;
        });

    /// <summary>
    /// Chooses the cover among the items of the story, or clears the choice so that the first item is the cover.
    /// </summary>
    public Task<StoryEditResult> SetCoverAsync(long storyId, long authorProfileId, long? itemId, CancellationToken cancellationToken) =>
        RunAsync(storyId, authorProfileId, cancellationToken, async (_, _, _) =>
        {
            if (itemId is not null && !await context.MediaLinks.StoryLinks().AnyAsync(l => l.StoryId == storyId && l.Id == itemId, cancellationToken))
            {
                return StoryEditorCodes.CoverInvalid;
            }

            await context.Stories.Where(s => s.Id == storyId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.CoverMediaLinkId, itemId), cancellationToken);
            return null;
        });

    /// <summary>
    /// Adds a tank, an inhabitant or a species the author may see. A tank or an inhabitant that is missing or
    /// that the author may not see is refused with one and the same answer.
    /// </summary>
    public Task<StoryEditResult> AddSubjectAsync(long storyId, long authorProfileId, AddStorySubjectRequest request, CancellationToken cancellationToken) =>
        RunAsync(storyId, authorProfileId, cancellationToken, async (_, _, _) =>
        {
            if (!await TargetIsAvailableAsync(request.Type, request.Id, authorProfileId, cancellationToken))
            {
                return StoryEditorCodes.SubjectNotFound;
            }

            var subjects = context.StorySubjects.Where(s => s.StoryId == storyId);
            var exists = request.Type switch
            {
                StorySubjectType.Tank => await subjects.AnyAsync(s => s.TankId == request.Id, cancellationToken),
                StorySubjectType.Inhabitant => await subjects.AnyAsync(s => s.InhabitantId == request.Id, cancellationToken),
                _ => await subjects.AnyAsync(s => s.SpeciesId == request.Id, cancellationToken),
            };
            if (exists)
            {
                return StoryEditorCodes.SubjectExists;
            }

            if (await subjects.CountAsync(cancellationToken) >= MaxSubjects)
            {
                return StoryEditorCodes.SubjectLimit;
            }

            int? position = request.Positioned ? await NextPositionAsync(storyId, cancellationToken) : null;
            var subject = request.Type switch
            {
                StorySubjectType.Tank => StorySubject.ForTank(request.Id, position),
                StorySubjectType.Inhabitant => StorySubject.ForInhabitant(request.Id, position),
                _ => StorySubject.ForSpecies(request.Id, position),
            };
            subject.StoryId = storyId;
            context.StorySubjects.Add(subject);
            return null;
        });

    /// <summary>
    /// Turns a subject into a card at the end of the sequence or into a reference.
    /// </summary>
    public Task<StoryEditResult> PlaceSubjectAsync(long storyId, long authorProfileId, long subjectId, bool positioned, CancellationToken cancellationToken) =>
        RunAsync(storyId, authorProfileId, cancellationToken, async (_, _, _) =>
        {
            var position = await context.StorySubjects.Where(s => s.StoryId == storyId && s.Id == subjectId)
                .Select(s => new { s.Position }).SingleOrDefaultAsync(cancellationToken);
            if (position is null)
            {
                return StoryEditorCodes.SubjectNotFound;
            }

            if (positioned && position.Position is null)
            {
                var next = await NextPositionAsync(storyId, cancellationToken);
                await context.StorySubjects.Where(s => s.Id == subjectId)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.Position, (int?)next), cancellationToken);
            }
            else if (!positioned && position.Position is not null)
            {
                await context.StorySubjects.Where(s => s.Id == subjectId)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.Position, (int?)null), cancellationToken);
                await RenumberAsync(storyId, await CurrentOrderAsync(storyId, cancellationToken), cancellationToken);
            }

            return null;
        });

    /// <summary>
    /// Removes a subject from the story, whether the author sees it or not.
    /// </summary>
    public Task<StoryEditResult> RemoveSubjectAsync(long storyId, long authorProfileId, long subjectId, CancellationToken cancellationToken) =>
        RunAsync(storyId, authorProfileId, cancellationToken, async (_, _, _) =>
        {
            var removed = await context.StorySubjects.Where(s => s.StoryId == storyId && s.Id == subjectId).ExecuteDeleteAsync(cancellationToken);
            if (removed == 0)
            {
                return StoryEditorCodes.SubjectNotFound;
            }

            await RenumberAsync(storyId, await CurrentOrderAsync(storyId, cancellationToken), cancellationToken);
            return null;
        });

    /// <summary>
    /// Takes a published story out of the gallery and makes it a draft again. It keeps its address, its
    /// comments and its ratings, and its files stop being public.
    /// </summary>
    public async Task<StoryEditResult> WithdrawAsync(long storyId, long authorProfileId, CancellationToken cancellationToken)
    {
        List<long> itemIds;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            var state = await context.LockOwnStoryAsync(storyId, authorProfileId, cancellationToken);
            if (state is null)
            {
                return StoryEditResult.Fail(StoryEditorCodes.StoryNotFound);
            }

            if (state != StoryState.Published)
            {
                return StoryEditResult.Fail(StoryEditorCodes.NotPublished);
            }

            var now = clock.GetUtcNow();
            await context.Stories.Where(s => s.Id == storyId).ExecuteUpdateAsync(
                set => set.SetProperty(s => s.State, StoryState.Draft).SetProperty(s => s.PublishedAt, (DateTimeOffset?)null)
                    .SetProperty(s => s.UpdatedAt, now), cancellationToken);
            itemIds = await context.MediaItemIdsOfStory(storyId).ToListAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await placement.PlaceNowAsync(itemIds);
        return new StoryEditResult(null, await query.GetAsync(storyId, authorProfileId, cancellationToken));
    }

    /// <summary>
    /// Deletes a story of the author. A story that was never published goes with its links. A story that was
    /// published has an address, and perhaps comments and ratings, that stay for the record: it is removed
    /// from the gallery and loses its links. The files stay in the library, and a file that the story was the
    /// quick share of can be shared again. Returns false when the author has no such story.
    /// </summary>
    public async Task<bool> DeleteAsync(long storyId, long authorProfileId, CancellationToken cancellationToken)
    {
        List<long> itemIds;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            var state = await context.LockOwnStoryAsync(storyId, authorProfileId, cancellationToken);
            if (state is null)
            {
                return false;
            }

            itemIds = await context.MediaItemIdsOfStory(storyId).ToListAsync(cancellationToken);
            if (!await context.SlugAliases.AnyAsync(a => a.StoryId == storyId, cancellationToken))
            {
                await context.Stories.Where(s => s.Id == storyId).ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                var now = clock.GetUtcNow();
                await context.MediaLinks.StoryLinks().Where(l => l.StoryId == storyId).ExecuteDeleteAsync(cancellationToken);
                await context.Stories.Where(s => s.Id == storyId).ExecuteUpdateAsync(
                    set => set.SetProperty(s => s.DeletedAt, (DateTimeOffset?)now).SetProperty(s => s.DeleteReason, "author_deleted")
                        .SetProperty(s => s.DeletedByProfileId, (long?)authorProfileId).SetProperty(s => s.UpdatedAt, now), cancellationToken);
                await context.MediaItems.Where(m => m.QuickShareStoryId == storyId)
                    .ExecuteUpdateAsync(set => set.SetProperty(m => m.QuickShareStoryId, (long?)null), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await placement.PlaceNowAsync(itemIds);
        return true;
    }

    /// <summary>
    /// Shares a file of the author to the gallery: creates a published story with the file as its only item.
    /// A file has at most one such story. Returns the problem code or the detail of the new story.
    /// </summary>
    public async Task<(StoryDetailDto? Story, string? Error)> QuickShareAsync(
        long mediaItemId, long authorProfileId, string title, string? description, StoryTopic topic, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        long storyId;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            if (await CheckOwnFileAsync(mediaItemId, authorProfileId, cancellationToken) is { } fileError)
            {
                return (null, fileError);
            }

            var marker = await context.MediaItems.Where(m => m.Id == mediaItemId).Select(m => m.QuickShareStoryId).SingleAsync(cancellationToken);
            if (marker is { } existing && await context.Stories.AnyAsync(s => s.Id == existing && s.DeletedAt == null, cancellationToken))
            {
                return (null, StoryEditorCodes.MediaAlreadyShared);
            }

            var story = new Story
            {
                AuthorProfileId = authorProfileId, Title = title, Description = description, Topic = topic, State = StoryState.Published,
                CreatedAt = now, UpdatedAt = now, PublishedAt = now,
            };
            context.Stories.Add(story);
            await context.SaveChangesAsync(cancellationToken);
            var link = MediaLink.ForStory(story.Id, mediaItemId);
            link.CreatedAt = now;
            context.MediaLinks.Add(link);
            await context.SaveChangesAsync(cancellationToken);
            await context.MediaItems.Where(m => m.Id == mediaItemId)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.QuickShareStoryId, (long?)story.Id), cancellationToken);
            await publication.RecordPublicationAsync(story.Id, authorProfileId, now, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            storyId = story.Id;
        }

        await placement.PlaceNowAsync([mediaItemId]);
        return (await stories.GetDetailAsync(storyId, authorProfileId, cancellationToken), null);
    }

    private async Task<StoryEditResult> RunAsync(
        long storyId, long authorProfileId, CancellationToken cancellationToken,
        Func<StoryState, DateTimeOffset, List<long>, Task<string?>> edit)
    {
        var placeIds = new List<long>();
        var published = false;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            var state = await context.LockOwnStoryAsync(storyId, authorProfileId, cancellationToken);
            if (state is null)
            {
                return StoryEditResult.Fail(StoryEditorCodes.StoryNotFound);
            }

            var now = clock.GetUtcNow();
            if (await edit(state.Value, now, placeIds) is { } error)
            {
                return StoryEditResult.Fail(error);
            }

            await context.SaveChangesAsync(cancellationToken);
            await context.Stories.Where(s => s.Id == storyId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.UpdatedAt, now), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            published = state == StoryState.Published;
        }

        if (published)
        {
            await placement.PlaceNowAsync(placeIds);
        }

        return new StoryEditResult(null, await query.GetAsync(storyId, authorProfileId, cancellationToken));
    }

    // The file must belong to the author, which also locks its row against a concurrent deletion, and a video
    // must be ready.
    private async Task<string?> CheckOwnFileAsync(long mediaItemId, long authorProfileId, CancellationToken cancellationToken)
    {
        var id = await context.MediaItems
            .FromSqlInterpolated($"SELECT * FROM public.media_item WHERE id = {mediaItemId} AND owner_profile_id = {authorProfileId} FOR SHARE")
            .Select(m => (long?)m.Id).SingleOrDefaultAsync(cancellationToken);
        if (id is null)
        {
            return StoryEditorCodes.MediaNotFound;
        }

        var ready = await context.MediaItems.AsNoTracking().Where(m => m.Id == mediaItemId)
            .Select(m => m.Kind == MediaKind.Photo
                || (m.VideoJob != null && m.VideoJob.Status == VideoJobStatus.Done && m.Variants.Any(v => v.Label == VideoStorage.HlsLabel)))
            .SingleAsync(cancellationToken);
        return ready ? null : StoryEditorCodes.MediaNotReady;
    }

    private async Task<bool> TargetIsAvailableAsync(StorySubjectType type, long id, long authorProfileId, CancellationToken cancellationToken)
    {
        var visibleTanks = context.TanksVisibleTo(authorProfileId).Where(t => t.State == TankState.Published && t.DeletedAt == null);
        return type switch
        {
            StorySubjectType.Tank => await visibleTanks.AnyAsync(t => t.Id == id, cancellationToken),
            StorySubjectType.Inhabitant => await context.Inhabitants.AnyAsync(
                i => i.Id == id && visibleTanks.Any(t => t.Id == i.TankId), cancellationToken),
            _ => await context.Species.AnyAsync(s => s.Id == id, cancellationToken),
        };
    }

    private static bool TryNormalizeCaption(string? raw, out string? caption)
    {
        caption = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        return caption is null || caption.Length <= MaxCaptionLength;
    }

    private async Task<int> NextPositionAsync(long storyId, CancellationToken cancellationToken)
    {
        var items = await context.MediaLinks.StoryLinks().Where(l => l.StoryId == storyId).MaxAsync(l => (int?)l.Position, cancellationToken);
        var cards = await context.StorySubjects.Where(s => s.StoryId == storyId).MaxAsync(s => s.Position, cancellationToken);
        return Math.Max(items ?? -1, cards ?? -1) + 1;
    }

    // The sequence as stored: items and positioned subjects by position, an item before a subject at the same
    // position.
    private async Task<List<StoryOrderEntry>> CurrentOrderAsync(long storyId, CancellationToken cancellationToken)
    {
        var items = await context.MediaLinks.StoryLinks().AsNoTracking().Where(l => l.StoryId == storyId)
            .Select(l => new { l.Id, l.Position }).ToListAsync(cancellationToken);
        var cards = await context.StorySubjects.AsNoTracking().Where(s => s.StoryId == storyId && s.Position != null)
            .Select(s => new { s.Id, Position = s.Position!.Value }).ToListAsync(cancellationToken);
        return items.Select(i => (i.Position, Kind: 0, i.Id, Entry: new StoryOrderEntry(StoryEntryKind.Item, i.Id)))
            .Concat(cards.Select(c => (c.Position, Kind: 1, c.Id, Entry: new StoryOrderEntry(StoryEntryKind.Subject, c.Id))))
            .OrderBy(e => e.Position).ThenBy(e => e.Kind).ThenBy(e => e.Id).Select(e => e.Entry).ToList();
    }

    private async Task RenumberAsync(long storyId, IReadOnlyList<StoryOrderEntry> order, CancellationToken cancellationToken)
    {
        await context.MediaLinks.StoryLinks().Where(l => l.StoryId == storyId)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.Position, l => l.Position + RenumberShift), cancellationToken);
        await context.StorySubjects.Where(s => s.StoryId == storyId && s.Position != null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Position, s => s.Position + RenumberShift), cancellationToken);
        for (var position = 0; position < order.Count; position++)
        {
            var (kind, id) = (order[position].Kind, order[position].Id);
            var target = position;
            if (kind == StoryEntryKind.Item)
            {
                await context.MediaLinks.Where(l => l.Id == id && l.StoryId == storyId)
                    .ExecuteUpdateAsync(set => set.SetProperty(l => l.Position, target), cancellationToken);
            }
            else
            {
                await context.StorySubjects.Where(s => s.Id == id && s.StoryId == storyId)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.Position, (int?)target), cancellationToken);
            }
        }
    }
}
