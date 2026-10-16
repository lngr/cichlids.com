using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Decommissioning;

/// <summary>
/// One decommissioning of a tank in the local dates of its owner. StartImage shows the empty tank and
/// EndImage the filled tank; EndDate and EndImage are null while the decommissioning is active.
/// </summary>
public sealed record DecommissioningDto(long Id, DecommissionScope Scope, string StartDate, string? EndDate, bool Active,
    ImageUrlsDto? StartImage, ImageUrlsDto? EndImage);

/// <summary>
/// The operating time of a tank and its decommissionings, the newest first. OperatingDays are the days
/// since the creation of the tank without the decommissioned ones, OperatingMonths the whole calendar
/// months they span and DecommissionedDays the days the tank was decommissioned. Decommissioned tells
/// whether a decommissioning is active.
/// </summary>
public sealed record TankOperationDto(long TankId, int OperatingDays, int OperatingMonths, int DecommissionedDays, bool Decommissioned,
    IReadOnlyList<DecommissioningDto> Items);
