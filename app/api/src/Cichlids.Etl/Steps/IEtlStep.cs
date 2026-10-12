using Cichlids.Etl.Runtime;

namespace Cichlids.Etl.Steps;

/// <summary>
/// One unit of the legacy data migration: reads from the legacy database through the context's
/// legacy connection and writes into the target through the context's current transaction,
/// reporting every read, write and skip into the context's statistics. A step commits nothing
/// itself, so its writes become part of whichever transaction it runs in.
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
