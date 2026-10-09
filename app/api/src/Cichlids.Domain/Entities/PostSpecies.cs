namespace Cichlids.Domain.Entities;

/// <summary>
/// Links a post to a species it depicts, ordered within the post's species set.
/// </summary>
public class PostSpecies
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public long SpeciesId { get; set; }
    public int Sort { get; set; }
}
