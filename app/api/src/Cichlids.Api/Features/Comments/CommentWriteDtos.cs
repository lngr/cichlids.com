namespace Cichlids.Api.Features.Comments;

/// <summary>
/// Body of a comment post request. At least one of the two fields must be provided: a non-empty
/// body creates a comment, stars (1 to 5) create a rating, and both together create one of each,
/// the same split the legacy data model migrated into. A member has one rating per target, so stars
/// replace an earlier rating of the member. Stars on own content are refused with 403.
/// </summary>
public sealed record CreateCommentRequest(string? Body, int? Stars);

/// <summary>
/// Confirmation of the rating a comment post request created or replaced.
/// </summary>
public sealed record CreatedRatingDto(long Id, short Stars);

/// <summary>
/// Result of a comment post request: the created comment shaped like a comment list item (null
/// for a rating-only request) and the created rating (null for a body-only request).
/// </summary>
public sealed record CommentCreatedDto(CommentDto? Comment, CreatedRatingDto? Rating);

/// <summary>
/// Body of a moderator comment deletion. The reason is required and becomes part of the
/// moderation trail on the comment row.
/// </summary>
public sealed record DeleteCommentRequest(string? Reason);
