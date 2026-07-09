using Cichlids.Api.Features.Common;

namespace Cichlids.Api.Features.Comments;

/// <summary>
/// A visible comment on a post or a tank. Exactly one of <see cref="Author"/> and
/// <see cref="PosterName"/> is set: migrated comments whose original author could not be matched
/// to a profile keep only the legacy display name.
/// </summary>
public sealed record CommentDto(long Id, string Body, DateTimeOffset CreatedAt, int Score, AuthorDto? Author, string? PosterName);
