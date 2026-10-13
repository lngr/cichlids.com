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
            new SpeciesLinksStep(),
            new CommentMigrationStep(),
            new GalleryMigrationStep(),
            new ForumMigrationStep(),
            new VerifyStep(),
        }.OrderBy(step => step.Order),
    ];

    /// <summary>
    /// Every step of All except the verification, in the same order.
    /// </summary>
    public static IReadOnlyList<IEtlStep> WriteSteps { get; } = [.. All.Where(step => step is not VerifyStep)];

    /// <summary>
    /// The verification step of All.
    /// </summary>
    public static IEtlStep Verify { get; } = All.Single(step => step is VerifyStep);

    public static IEtlStep? Find(string name) =>
        All.FirstOrDefault(step => string.Equals(step.Name, name, StringComparison.OrdinalIgnoreCase));
}
