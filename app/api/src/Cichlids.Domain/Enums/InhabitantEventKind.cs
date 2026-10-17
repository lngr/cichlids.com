namespace Cichlids.Domain.Enums;

/// <summary>
/// The kinds of events on the timeline of a named fish: a measured length (growth), a spawning with the
/// generation of the offspring, a move to another tank of the owner and the death of the fish.
/// </summary>
public enum InhabitantEventKind { Growth, Spawning, Moved, Died }
