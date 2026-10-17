using Cichlids.Domain;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// A milestone of an own tank that shows on Today. Key is the stable key to dismiss it with. Value is
/// the streak length in weeks of a kept streak, the number of the full tank year of an anniversary and
/// 0 for the first care week. ReachedOn is the local date the milestone was reached.
/// </summary>
public sealed record MilestoneDto(string Key, MilestoneKind Kind, long TankId, string TankTitle, int Value, string ReachedOn);

/// <summary>
/// The milestones of the own tanks that were reached within the last 7 days and are not dismissed.
/// </summary>
public sealed record MilestonesDto(IReadOnlyList<MilestoneDto> Items);
