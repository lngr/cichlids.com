using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Stories;

/// <summary>
/// A story in the gallery list. Cover shows the image of the cover item and is null when that item
/// has no image; ItemCount is the number of items of the story.
/// </summary>
public sealed record StoryListItemDto(
    long Id,
    string Slug,
    string? Title,
    string? Description,
    DateTimeOffset? PublishedAt,
    long ViewCount,
    double? RatingAverage,
    int RatingCount,
    int CommentCount,
    StoryTopic Topic,
    AuthorDto Author,
    ImageUrlsDto? Cover,
    int ItemCount);

/// <summary>
/// One item of a story: a photo or a video with the caption that belongs to this item only. Image is
/// set for a photo and Video for a video. Position orders the item among the items and the positioned
/// subjects of the story.
/// </summary>
public sealed record StoryItemDto(
    long Id, long MediaItemId, int Position, string? Caption, MediaKind Kind, ImageUrlsDto? Image, VideoDto? Video);

/// <summary>
/// The tank a story subject points to, as a card.
/// </summary>
public sealed record StorySubjectTankDto(long Id, string Title, TankCategory? Category, ImageUrlsDto? Image);

/// <summary>
/// The inhabitant a story subject points to, as a card: its species, its count and the tank it lives in.
/// </summary>
public sealed record StorySubjectInhabitantDto(long Id, long TankId, string TankTitle, InhabitantSpeciesRefDto? Species, int? Count);

/// <summary>
/// The species a story subject points to, as a card.
/// </summary>
public sealed record StorySubjectSpeciesDto(long Id, string? Slug, string Genus, string Name, string DisplayName);

/// <summary>
/// A subject of a story. Type names the kind and exactly the matching card is set. With a position the
/// subject is a card within the sequence of items; without one it is a reference shown with the story.
/// </summary>
public sealed record StorySubjectDto(
    long Id,
    StorySubjectType Type,
    int? Position,
    StorySubjectTankDto? Tank,
    StorySubjectInhabitantDto? Inhabitant,
    StorySubjectSpeciesDto? Species);

/// <summary>
/// A story with its items in order and the subjects the viewer may see. Subjects that point to a tank or
/// an inhabitant the viewer may not see are not part of the response.
/// </summary>
public sealed record StoryDetailDto(
    long Id,
    string Slug,
    string CanonicalSlug,
    string? Title,
    string? Description,
    DateTimeOffset? PublishedAt,
    long ViewCount,
    double? RatingAverage,
    int RatingCount,
    int CommentCount,
    StoryTopic Topic,
    AuthorDto Author,
    ImageUrlsDto? Cover,
    long? CoverItemId,
    IReadOnlyList<StoryItemDto> Items,
    IReadOnlyList<StorySubjectDto> Subjects);
