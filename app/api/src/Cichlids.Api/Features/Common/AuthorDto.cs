namespace Cichlids.Api.Features.Common;

/// <summary>
/// The attribution shown for a post, tank or comment: the minimal public identity of its author
/// profile.
/// </summary>
public sealed record AuthorDto(long Id, string Username, string? DisplayName, string? AvatarUrl);
