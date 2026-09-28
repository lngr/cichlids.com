using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// An unpublished single-photo post of the caller: its id for the publish and discard calls, its
/// state (always draft), the topic it defaults to, when it was uploaded and the public URLs of
/// its photo renditions.
/// </summary>
public sealed record DraftDto(long Id, PostState State, PostTopic Topic, DateTimeOffset CreatedAt, ImageUrlsDto Image);

/// <summary>
/// Body of a publish request. The title is required (1 to 200 characters after trimming), the
/// description is optional (at most 5000 characters) and the topic is one of cichlids, tanks or
/// offtopic, cichlids when omitted.
/// </summary>
public sealed record PublishPostRequest(string? Title, string? Description, string? Topic);
