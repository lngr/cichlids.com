namespace Cichlids.Domain.Entities;

/// <summary>
/// Common (vernacular) name of a species. A species can have more than one, across languages
/// or regions.
/// </summary>
public class SpeciesCommonName
{
    public long Id { get; set; }
    public long SpeciesId { get; set; }
    public string Name { get; set; } = string.Empty;
}
