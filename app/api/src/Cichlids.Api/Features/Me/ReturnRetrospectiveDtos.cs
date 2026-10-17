using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// A care rule of an own tank whose due date has passed. DaysOverdue counts the days since the due date.
/// </summary>
public sealed record ReturnOverdueCareItemDto(long TankId, string TankTitle, CareKind Kind, int DaysOverdue);

/// <summary>
/// The overdue care of the own tanks, the longest overdue first.
/// </summary>
public sealed record ReturnOverdueCareDto(IReadOnlyList<ReturnOverdueCareItemDto> Items);

/// <summary>
/// What other members did with the content of the member since the absence began: Kudos is the number of
/// positive votes on own comments, Comments the number of comments on own published stories and tanks.
/// </summary>
public sealed record ReturnResponsesDto(int Kudos, int Comments);

/// <summary>
/// A new public tank in a region the member is interested in.
/// </summary>
public sealed record ReturnRegionTankDto(long TankId, string Title, TankRegion? Region, AuthorDto Owner);

/// <summary>
/// A new published story about public tanks in a region the member is interested in.
/// </summary>
public sealed record ReturnRegionStoryDto(long Id, string? Slug, string? Title, AuthorDto Author);

/// <summary>
/// New public tanks and stories in the regions of the member, newest first, at most five of each.
/// </summary>
public sealed record ReturnRegionNewsDto(IReadOnlyList<ReturnRegionTankDto> Tanks, IReadOnlyList<ReturnRegionStoryDto> Stories);

/// <summary>
/// An own tank a next step refers to.
/// </summary>
public sealed record ReturnTankRefDto(long TankId, string Title);

/// <summary>
/// The next steps of a return: CareTanks are the own tanks with overdue care (log the missed care),
/// TimeLapseTanks the own tanks with a time-lapse (take a current photo with the guided camera).
/// </summary>
public sealed record ReturnNextStepsDto(IReadOnlyList<ReturnTankRefDto> CareTanks, IReadOnlyList<ReturnTankRefDto> TimeLapseTanks);

/// <summary>
/// The return retrospective of a member. Since is the start of the absence it covers. Seen tells whether
/// the member already saw it. A card without data is null; the retrospective exists only with at least one
/// card of data, and NextSteps is set only then.
/// </summary>
public sealed record ReturnRetrospectiveDto(
    bool Seen,
    DateTimeOffset Since,
    ReturnOverdueCareDto? OverdueCare,
    ReturnResponsesDto? Responses,
    TodayFollowedDto? FollowedTanks,
    ReturnRegionNewsDto? RegionNews,
    ReturnNextStepsDto? NextSteps);

/// <summary>
/// Whether the member has a return retrospective to open: one that is due or one seen within the last 30
/// days. SeenAt is the time the member saw it, null while it is unseen.
/// </summary>
public sealed record ReturnRetrospectiveStatusDto(bool Available, DateTimeOffset? SeenAt);
