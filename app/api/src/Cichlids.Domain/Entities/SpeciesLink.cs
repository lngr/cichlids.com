namespace Cichlids.Domain.Entities;

/// <summary>
/// External reference link attached to a species (for example a care sheet or source).
/// </summary>
public class SpeciesLink
{
    public long Id { get; set; }
    public long SpeciesId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int Sort { get; set; }
}
