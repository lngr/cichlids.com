using Cichlids.Api.Features.Common;

namespace Cichlids.Api.Features.Comments;

/// <summary>
/// A visible comment on a post or a tank. Exactly one of Author and PosterName is set: migrated
/// comments whose original author could not be matched to a profile keep only the legacy display
/// name. Stars is the rating the author gave together with the comment, null when the comment came
/// without one.
/// </summary>
public sealed record CommentDto(
    long Id, string Body, DateTimeOffset CreatedAt, int Score, short? Stars, AuthorDto? Author, string? PosterName);
