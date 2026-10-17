using Cichlids.Api.Features.Media;

namespace Cichlids.Api.Features.Retrospectives;

/// <summary>
/// The archive retrospective of a member: one entry per card with its data, in the order the app
/// presents them (member since, numbers, most viewed story, archive tanks). A card without data is
/// null or empty.
/// </summary>
public sealed record ArchiveRetrospectiveDto(
    ArchiveMemberSinceDto? MemberSince,
    ArchiveStatsDto? Stats,
    ArchiveMostViewedDto? MostViewed,
    IReadOnlyList<ArchiveTankDto> Tanks);

/// <summary>
/// The year and month the member joined the archive.
/// </summary>
public sealed record ArchiveMemberSinceDto(int Year, int Month);

/// <summary>
/// The number of migrated photos of the member, the comments of other members on the stories and tanks
/// of the member, and the stars of the ratings of other members on them.
/// </summary>
public sealed record ArchiveStatsDto(int Photos, int Comments, int Stars);

/// <summary>
/// The story of the member with the highest view count: its id, canonical slug, title, view count and
/// cover image (null when the story has no image).
/// </summary>
public sealed record ArchiveMostViewedDto(long StoryId, string Slug, string? Title, long ViewCount, ImageUrlsDto? Image);

/// <summary>
/// An archive tank of the member that is not adopted yet, with its image (null when the tank has none).
/// </summary>
public sealed record ArchiveTankDto(long Id, string Title, ImageUrlsDto? Image);

/// <summary>
/// Whether the archive retrospective of the member exists, and when the member first saw it (null while unseen).
/// </summary>
public sealed record ArchiveRetrospectiveStatusDto(bool Available, DateTimeOffset? SeenAt);
