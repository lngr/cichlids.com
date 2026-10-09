namespace Cichlids.Domain.Entities;

/// <summary>
/// A stocking entry of a tank: an optional species reference and a count, ordered within the
/// tank's stocking list.
/// </summary>
public class Inhabitant
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public long? SpeciesId { get; set; }
    public int? Count { get; set; }
    public int Sort { get; set; }
}
