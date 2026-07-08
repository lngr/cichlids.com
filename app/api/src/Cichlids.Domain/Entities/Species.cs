using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// Species catalog entry with care ranges and classification used for stocking guidance.
/// </summary>
public class Species
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public string Genus { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? TemperatureRange { get; set; }
    public string? PhRange { get; set; }
    public string? GhRange { get; set; }
    public string? KhRange { get; set; }
    public string? MaxSize { get; set; }
    public SpeciesBreeding Breeding { get; set; } = SpeciesBreeding.Unspecified;
    public AggressionLevel Aggression { get; set; } = AggressionLevel.Unspecified;
    public AggressionLevel IntraAggression { get; set; } = AggressionLevel.Unspecified;
    public SpeciesDiet Diet { get; set; } = SpeciesDiet.Unspecified;
    public string? Morphs { get; set; }
    public string? Description { get; set; }
    public string? Origin { get; set; }
    public string? Habitat { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<SpeciesCommonName> CommonNames { get; set; } = [];
    public List<SpeciesLink> Links { get; set; } = [];
}
