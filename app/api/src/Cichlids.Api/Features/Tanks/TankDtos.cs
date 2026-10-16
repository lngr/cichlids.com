using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// A tank in a list. Decommissioned tells whether the tank is decommissioned at the moment; it is false
/// for viewers who do not see the care stream of the tank.
/// </summary>
public sealed record TankListItemDto(
    long Id, string Title, TankCategory? Category, ImageUrlsDto? MainImage, int ImageCount, AuthorDto Author, TankRegion? Region = null,
    TankVisibility ProfileVisibility = TankVisibility.Public, bool Decommissioned = false);

public sealed record WaterValuesDto(string? Ph, string? Kh, string? Gh, string? No2, string? No3, string? Po4, string? Notes);

public sealed record DimensionsDto(
    decimal? Width, decimal? Height, decimal? Depth, DimensionUnit? Unit,
    decimal? InputWidth, decimal? InputHeight, decimal? InputDepth, DimensionUnit? InputUnit);

public sealed record VolumeDto(decimal Liters, decimal? InputValue, VolumeUnit? InputUnit);

public sealed record DimensionInputDto(decimal? Width, decimal? Height, decimal? Depth, DimensionUnit Unit);

public sealed record VolumeInputDto(decimal Value, VolumeUnit Unit);

/// <summary>
/// The request to create a tank. IsPublic is the answer to the question whether the tank should be public;
/// it has no default, and a request without it is refused.
/// </summary>
public sealed record CreateTankRequest(
    string Title, DimensionInputDto? Dimensions, VolumeInputDto? Volume, TankRegion? Region = null, bool? IsPublic = null);

/// <summary>
/// The audience levels of a tank: its profile and its care stream, which is never wider than the profile.
/// The levels in order of breadth are public, followers, selected and private.
/// </summary>
public sealed record TankVisibilityDto(TankVisibility Profile, TankVisibility Stream);

/// <summary>
/// The new levels of a tank, as the lower snake_case names public, followers, selected and private. Both
/// are required.
/// </summary>
public sealed record UpdateTankVisibilityRequest(string? Profile, string? Stream);

public sealed record TankMediaItemDto(long MediaItemId, ImageUrlsDto Image);

public sealed record TankSectionsDto(
    IReadOnlyList<TankMediaItemDto> Showcase,
    IReadOnlyList<TankMediaItemDto> Decoration,
    IReadOnlyList<TankMediaItemDto> Technic);

public sealed record InhabitantSpeciesRefDto(long Id, string? Slug, string Genus, string Name, string DisplayName);

public sealed record InhabitantDto(InhabitantSpeciesRefDto? Species, int? Count);

public sealed record TankDetailDto(
    long Id,
    string Title,
    TankCategory? Category,
    string? Description,
    string? Gravel,
    string? Plants,
    string? Decoration,
    string? Light,
    string? LightDuration,
    string? Filtration,
    string? Technic,
    WaterValuesDto WaterValues,
    string? Food,
    string? Notes,
    DimensionsDto? Dimensions,
    ImageUrlsDto? MainImage,
    TankSectionsDto Sections,
    IReadOnlyList<InhabitantDto> Inhabitants,
    AuthorDto Author,
    VolumeDto? Volume,
    TankVisibilityDto Visibility,
    TankRegion? Region = null,
    TankFollowDto? Follow = null,
    TankAudienceDto? Audience = null);
