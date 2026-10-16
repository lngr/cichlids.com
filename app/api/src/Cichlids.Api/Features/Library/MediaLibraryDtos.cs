using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Library;

/// <summary>
/// The kind of place a file of the media library is linked to.
/// </summary>
public enum MediaPlaceKind
{
    Tank,
    TankMain,
    DecommissionProof,
    Inhabitant,
    CareEntry,
    WaterChange,
    Story,
    DiscussionPost,
    ProfileAvatar,
    ProfileHeader,
}

/// <summary>
/// A place that shows a file. The name and the ids are set only for places of the member who owns the
/// file: the title of a tank, the title of a story, the title of a discussion thread. A place of another
/// member has the kind alone. The section names the part of a tank, the care kind and the time belong to a
/// care entry or a water change, and the slug addresses a published story.
/// </summary>
public sealed record MediaPlaceDto(
    MediaPlaceKind Kind, string? Name, long? TankId, long? CareEntryId, long? StoryId, string? StorySlug,
    TankMediaSection? Section, CareKind? CareKind, DateTimeOffset? At);

/// <summary>
/// A file of the media library. A photo has image URLs, a video has its video representation; both are
/// signed for the owner. PlaceCount is the number of places that show the file and PlaceKinds their
/// distinct kinds; a file without places is not linked.
/// </summary>
public sealed record MediaLibraryItemDto(
    long Id, MediaKind Kind, DateTimeOffset CreatedAt, int? Width, int? Height, ImageUrlsDto? Image, VideoDto? Video,
    int PlaceCount, IReadOnlyList<MediaPlaceKind> PlaceKinds);

/// <summary>
/// A time-lapse week of a tank that a file keeps: the file is the only in-app photo of that ISO week
/// (WeekStart is its Monday) within the current run of the streak.
/// </summary>
public sealed record MediaStreakWeekDto(long TankId, string TankTitle, string WeekStart);

/// <summary>
/// A file of the media library with its provenance and the places that show it. CanDelete is false for a
/// file that proves a decommissioning of a tank. KeepsStreak lists the time-lapse weeks the file keeps, so
/// deleting it shortens or breaks the streak of that tank.
/// </summary>
public sealed record MediaLibraryDetailDto(
    MediaLibraryItemDto Item, string? OriginalFilename, long? ByteSize, bool CapturedInApp, DateTimeOffset ReceivedAt,
    DateTimeOffset? TakenAt, bool CanDelete, IReadOnlyList<MediaPlaceDto> Places, IReadOnlyList<MediaStreakWeekDto> KeepsStreak);

/// <summary>
/// The places a deleted file was linked to.
/// </summary>
public sealed record MediaLibraryDeletedDto(IReadOnlyList<MediaPlaceDto> Places);
