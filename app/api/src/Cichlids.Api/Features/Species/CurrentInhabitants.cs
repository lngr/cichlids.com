using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;

namespace Cichlids.Api.Features.Species;

/// <summary>
/// The single definition of which inhabitant rows count as the current stocking of a tank. Every
/// query that asks which species a tank keeps reads its rows from here.
/// </summary>
public static class CurrentInhabitants
{
    /// <summary>
    /// The inhabitant rows that belong to the current stocking: those that have not been removed.
    /// </summary>
    public static IQueryable<Inhabitant> Query(CichlidsDbContext context) => context.Inhabitants.Where(i => i.RemovedOn == null);
}
