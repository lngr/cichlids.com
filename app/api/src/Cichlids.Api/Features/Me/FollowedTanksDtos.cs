using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// What a followed tank published: a story pointing to the tank, a photo of its time-lapse, a photo
/// of one of its sections or an entry of its care stream.
/// </summary>
public enum FollowedUpdateKind { Story, TimeLapsePhoto, TankPhoto, CareEntry }

/// <summary>
/// The story of an update, as far as the viewer needs to open it.
/// </summary>
public sealed record FollowedStoryDto(long Id, string? Slug, string? Title);

/// <summary>
/// The newest update of a followed tank that the viewer may see. At is the moment the update was
/// published or recorded. CareKind is set for a care entry, Story for a story and Image for a photo
/// update whose file the viewer may use.
/// </summary>
public sealed record FollowedUpdateDto(FollowedUpdateKind Kind, DateTimeOffset At, CareKind? CareKind, FollowedStoryDto? Story, ImageUrlsDto? Image);

/// <summary>
/// A followed tank the viewer sees with its owner and its newest visible update. Update is null for a
/// tank without a visible update.
/// </summary>
public sealed record FollowedTankDto(long TankId, string Title, AuthorDto Owner, FollowedUpdateDto? Update);

/// <summary>
/// The followed tanks with a visible update in the last 30 days, newest first and at most five.
/// HasMore tells whether more followed tanks have such an update.
/// </summary>
public sealed record TodayFollowedDto(IReadOnlyList<FollowedTankDto> Items, bool HasMore);
