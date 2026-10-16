namespace Cichlids.Domain.Enums;

/// <summary>
/// The kind of thing a story subject points to. Each type has its own key column in the subject
/// table.
/// </summary>
public enum StorySubjectType
{
    Tank,
    Inhabitant,
    Species,
}
