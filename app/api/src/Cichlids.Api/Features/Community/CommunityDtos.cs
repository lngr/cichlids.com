using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Community;

public sealed record CommunityCategoryDto(DiscussionCategory Category, int ThreadCount, int PostCount, DateTimeOffset? LastPostAt);

/// <summary>
/// Who opened a thread: a full profile reference for a real member, otherwise just the name to
/// show (an unlisted placeholder profile's display name, or a guest's own poster name).
/// </summary>
public sealed record ThreadStartedByDto(AuthorDto? ProfileRef, string? PosterName);

public sealed record CommunityThreadListItemDto(
    long Id,
    string Title,
    DiscussionCategory Category,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastPostAt,
    int PostCount,
    ThreadStartedByDto StartedBy);

/// <summary>
/// A post's author: <see cref="Id"/>, <see cref="Username"/> and <see cref="AvatarUrl"/> are only
/// set for a real member profile; every other post (an unlisted placeholder profile or a guest
/// with no profile at all) carries just <see cref="DisplayName"/>.
/// </summary>
public sealed record CommunityPostAuthorDto(long? Id, string? Username, string DisplayName, string? AvatarUrl);

public sealed record CommunityAttachmentDto(long MediaItemId, ImageUrlsDto Image);

public sealed record CommunityPostDto(
    long Id,
    string Body,
    DateTimeOffset CreatedAt,
    CommunityPostAuthorDto Author,
    IReadOnlyList<CommunityAttachmentDto> Attachments);

public sealed record CommunityThreadDetailDto(
    long Id,
    string Title,
    DiscussionCategory Category,
    DiscussionThreadState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastPostAt,
    int PostCount,
    IReadOnlyList<CommunityPostDto> Posts);
