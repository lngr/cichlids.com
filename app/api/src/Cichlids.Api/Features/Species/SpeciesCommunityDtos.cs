using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Species;

/// <summary>
/// A photo story that shows the species, with its cover and the figures that rank it.
/// </summary>
public sealed record SpeciesCommunityPhotoDto(
    long StoryId, string Slug, string? Title, ImageUrlsDto Image, long ViewCount, double? RatingAverage, int RatingCount);

/// <summary>
/// A public tank that keeps the species, as a card.
/// </summary>
public sealed record SpeciesCommunityTankDto(long Id, string Title, TankCategory? Category, ImageUrlsDto? Image);

/// <summary>
/// A species kept together with the requested one and the number of public tanks that keep both.
/// </summary>
public sealed record SpeciesStockingPartnerDto(InhabitantSpeciesRefDto Species, int TankCount);

/// <summary>
/// What the community does with a species, from public data only: the best photos, the number of
/// public tanks keeping it with a few of them, and the stocking partners found in at least three
/// public tanks.
/// </summary>
public sealed record SpeciesCommunityDto(
    IReadOnlyList<SpeciesCommunityPhotoDto> Photos,
    int TankCount,
    IReadOnlyList<SpeciesCommunityTankDto> Tanks,
    IReadOnlyList<SpeciesStockingPartnerDto> StockingPartners);
