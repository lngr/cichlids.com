namespace Cichlids.Etl.Steps;

/// <summary>
/// The fixed set of ETL steps, ordered deterministically so that "all" always runs them in the
/// same sequence regardless of how they were registered below.
/// </summary>
public static class EtlStepRegistry
{
    public static IReadOnlyList<IEtlStep> All { get; } =
    [
        .. new IEtlStep[]
        {
            new SpeciesCatalogStep(),
            new ProfileMigrationStep(),
            new TankMigrationStep(),
            new PictureMigrationStep(),
        }.OrderBy(step => step.Order),
    ];

    public static IEtlStep? Find(string name) =>
        All.FirstOrDefault(step => string.Equals(step.Name, name, StringComparison.OrdinalIgnoreCase));
}
