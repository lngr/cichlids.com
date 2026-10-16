using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Stories;

/// <summary>
/// Title, text and topic of a story in the editor. The title may stay empty while the story is a draft.
/// The description has at most 5000 characters and the topic is cichlids, tanks or offtopic, cichlids
/// when omitted.
/// </summary>
public sealed record StoryTextRequest(string? Title, string? Description, string? Topic);

/// <summary>
/// Adds a file of the caller to a story as its last entry, with an optional caption of at most 500
/// characters that belongs to this story only.
/// </summary>
public sealed record AddStoryItemRequest(long MediaItemId, string? Caption);

/// <summary>
/// Sets the caption of an item of a story; a blank caption removes it.
/// </summary>
public sealed record StoryCaptionRequest(string? Caption);

/// <summary>
/// Chooses the cover among the items of a story by the id of the item; null makes the first item the cover.
/// </summary>
public sealed record StoryCoverRequest(long? ItemId);

/// <summary>
/// What an entry of the sequence of a story is: an item or a positioned subject.
/// </summary>
public enum StoryEntryKind
{
    Item,
    Subject,
}

/// <summary>
/// One entry of the sequence of a story, named by its kind and its id.
/// </summary>
public sealed record StoryOrderEntry(StoryEntryKind Kind, long Id);

/// <summary>
/// The complete sequence of a story: every item and every positioned subject exactly once, in the wanted order.
/// </summary>
public sealed record StoryOrderRequest(IReadOnlyList<StoryOrderEntry>? Entries);

/// <summary>
/// Adds a tank, an inhabitant or a species to a story. Id is the id of the target of the given type.
/// Positioned makes the subject a card at the end of the sequence; otherwise it is a reference.
/// </summary>
public sealed record AddStorySubjectRequest(StorySubjectType Type, long Id, bool Positioned);

/// <summary>
/// Turns a subject of a story into a card at the end of the sequence or into a reference.
/// </summary>
public sealed record StorySubjectPlacementRequest(bool Positioned);

/// <summary>
/// A story of the caller as the editor shows it, in any state. Slug is set for a story that was published.
/// Items carry the ids of their links, which the editor routes use. Subjects lists the subjects the caller
/// may see; UnavailableSubjectIds names the subjects that point to a tank or an inhabitant the caller may
/// no longer see, so they can still be removed. QuickShareMediaItemId is the file this story was created
/// for by a quick share, or null.
/// </summary>
public sealed record StoryEditorDto(
    long Id,
    StoryState State,
    string? Slug,
    string? Title,
    string? Description,
    StoryTopic Topic,
    long? CoverItemId,
    ImageUrlsDto? Cover,
    IReadOnlyList<StoryItemDto> Items,
    IReadOnlyList<StorySubjectDto> Subjects,
    IReadOnlyList<long> UnavailableSubjectIds,
    long? QuickShareMediaItemId,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A story of the caller in the list of their stories. Cover shows the image of the cover item and is null
/// when the story has no item or the cover is a video.
/// </summary>
public sealed record MyStoryDto(
    long Id, StoryState State, string? Slug, string? Title, StoryTopic Topic, int ItemCount, ImageUrlsDto? Cover, DateTimeOffset UpdatedAt);
