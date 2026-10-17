using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Tanks;

public sealed record InhabitantSpeciesRefDto(long Id, string? Slug, string Genus, string Name, string DisplayName);

/// <summary>
/// An inhabitant of a tank: a group (species and count) or a named fish (name, count one, optional species).
/// MovedInOn and RemovedOn are calendar dates as YYYY-MM-DD; a migrated inhabitant has no MovedInOn and
/// counts as present since the tank was created. RemovedOn and RemovalReason are set together once the
/// inhabitant has left.
/// </summary>
public sealed record InhabitantDto(
    long Id, InhabitantSpeciesRefDto? Species, int? Count, string? Name, string? MovedInOn, string? RemovedOn,
    InhabitantRemovalReason? RemovalReason);

/// <summary>
/// The stocking of a tank: the inhabitants that live in it, named fish first, and the history of those
/// that left, the most recent departure first.
/// </summary>
public sealed record InhabitantListDto(IReadOnlyList<InhabitantDto> Current, IReadOnlyList<InhabitantDto> History);

/// <summary>
/// A new inhabitant. A group needs a species and a count; a named fish needs a name, has a count of one
/// and may have a species. MovedInOn is optional and defaults to the local date of the caller.
/// </summary>
public sealed record AddInhabitantRequest(long? SpeciesId, int? Count, string? Name, string? MovedInOn);

/// <summary>
/// The new values of a current inhabitant, validated like an addition. A MovedInOn that is left out keeps
/// the stored day.
/// </summary>
public sealed record UpdateInhabitantRequest(long? SpeciesId, int? Count, string? Name, string? MovedInOn);

/// <summary>
/// The removal of a current inhabitant. Reason is died, given_away or moved; RemovedOn is optional and
/// defaults to the local date of the caller.
/// </summary>
public sealed record RemoveInhabitantRequest(string? Reason, string? RemovedOn);
