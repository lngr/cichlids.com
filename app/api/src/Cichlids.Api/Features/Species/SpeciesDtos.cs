using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Species;

public sealed record SpeciesListItemDto(long Id, string? Slug, string Genus, string Name, string DisplayName, string? Category);

public sealed record SpeciesLinkDto(string Url, string? Label);

public sealed record SpeciesDetailDto(
    long Id,
    string? Slug,
    string Genus,
    string Name,
    string DisplayName,
    string? Category,
    string? TemperatureRange,
    string? PhRange,
    string? GhRange,
    string? KhRange,
    string? MaxSize,
    SpeciesBreeding Breeding,
    AggressionLevel Aggression,
    AggressionLevel IntraAggression,
    SpeciesDiet Diet,
    IReadOnlyList<string> CommonNames,
    IReadOnlyList<SpeciesLinkDto> Links,
    string? Description,
    string? Origin,
    string? Habitat,
    string? Morphs);
