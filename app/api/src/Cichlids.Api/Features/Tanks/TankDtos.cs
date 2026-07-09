using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Tanks;

public sealed record TankListItemDto(
    long Id, string Title, TankCategory? Category, ImageUrlsDto? MainImage, int ImageCount, AuthorDto Author);

public sealed record WaterValuesDto(string? Ph, string? Kh, string? Gh, string? No2, string? No3, string? Po4, string? Notes);

public sealed record DimensionsDto(int? Width, int? Height, int? Depth, DimensionUnit? Unit);

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
    AuthorDto Author);
