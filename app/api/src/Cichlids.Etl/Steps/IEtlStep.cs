using Cichlids.Etl.Runtime;

namespace Cichlids.Etl.Steps;

/// <summary>
/// One unit of the legacy data migration: reads from the legacy database through
/// <see cref="EtlContext.Legacy"/> and upserts into the target through <see cref="EtlContext.Db"/>,
/// reporting every read, write and skip into <see cref="EtlContext.Statistics"/>.
/// </summary>
public interface IEtlStep
{
    /// <summary>
    /// The name used to select this step from the command line and to key its statistics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Position in the deterministic run order for "all", lowest first. Steps with a foreign key
    /// dependency on another step's target table order after it.
    /// </summary>
    int Order { get; }

    Task RunAsync(EtlContext context, CancellationToken cancellationToken);
}
