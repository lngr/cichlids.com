using Cichlids.Domain;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// A milestone of an own tank or an own named fish that shows on Today. Key is the stable key to dismiss it
/// with. Value is the streak length in weeks of a kept streak, the number of the full year of an anniversary
/// and 0 for the first care week. ReachedOn is the local date the milestone was reached. TankId and TankTitle
/// name the tank, the tank the fish lives in for a fish anniversary; InhabitantId and InhabitantName are set
/// for a fish anniversary only.
/// </summary>
public sealed record MilestoneDto(
    string Key, MilestoneKind Kind, long TankId, string TankTitle, int Value, string ReachedOn, long? InhabitantId, string? InhabitantName);

/// <summary>
/// The milestones of the own tanks and named fish that were reached within the last 7 days and are not dismissed.
/// </summary>
public sealed record MilestonesDto(IReadOnlyList<MilestoneDto> Items);
