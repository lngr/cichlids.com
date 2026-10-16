using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// Points from a story to a tank, an inhabitant or a species. SubjectType names the target and
/// exactly the key column that matches it is set, which the database checks. With a position the
/// subject is a card within the sequence of the story, which items and cards share; without one it
/// is a reference shown with the story. Deleting the story or the target deletes the subject.
/// </summary>
public class StorySubject
{
    public long Id { get; set; }
    public long StoryId { get; set; }
    public StorySubjectType SubjectType { get; set; }
    public long? TankId { get; set; }
    public long? InhabitantId { get; set; }
    public long? SpeciesId { get; set; }
    public int? Position { get; set; }

    public static StorySubject ForTank(long tankId, int? position = null) =>
        new() { SubjectType = StorySubjectType.Tank, TankId = tankId, Position = position };

    public static StorySubject ForInhabitant(long inhabitantId, int? position = null) =>
        new() { SubjectType = StorySubjectType.Inhabitant, InhabitantId = inhabitantId, Position = position };

    public static StorySubject ForSpecies(long speciesId, int? position = null) =>
        new() { SubjectType = StorySubjectType.Species, SpeciesId = speciesId, Position = position };
}
