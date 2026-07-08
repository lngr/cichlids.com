using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Runtime;

/// <summary>
/// Shared state for one ETL invocation: the read-only legacy connection, the target database
/// context (and the raw connection it holds, reused for the upsert and bulk-insert helpers so a
/// step's writes and the migration-history check share one transaction), the dry-run flag and the
/// statistics collector every step reports into.
/// </summary>
public sealed class EtlContext : IAsyncDisposable
{
    private EtlContext(MySqlConnection legacy, CichlidsDbContext db, bool dryRun)
    {
        Legacy = legacy;
        Db = db;
        DryRun = dryRun;
    }

    public MySqlConnection Legacy { get; }

    public CichlidsDbContext Db { get; }

    public NpgsqlConnection Target => (NpgsqlConnection)Db.Database.GetDbConnection();

    public bool DryRun { get; }

    public EtlStatistics Statistics { get; } = new();

    /// <summary>
    /// Object store for steps that export legacy binary content (for example forum attachments)
    /// into object storage. Null when the current invocation has no object store configured; only
    /// steps that need it fail if it is missing.
    /// </summary>
    public IObjectStore? ObjectStore { get; set; }

    /// <summary>
    /// The transaction the current step's reads and writes run in, set by <see cref="EtlRunner"/>
    /// before invoking the step so steps never need to cast the ambient EF Core transaction back
    /// to the underlying ADO.NET type themselves.
    /// </summary>
    public NpgsqlTransaction Transaction { get; internal set; } = null!;

    public static async Task<EtlContext> CreateAsync(
        string legacyConnectionString,
        string targetConnectionString,
        bool dryRun,
        CancellationToken cancellationToken,
        IObjectStore? objectStore = null)
    {
        var legacy = new MySqlConnection(legacyConnectionString);
        await legacy.OpenAsync(cancellationToken);

        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(targetConnectionString)
            .UseSnakeCaseNamingConvention();
        var db = new CichlidsDbContext(optionsBuilder.Options);
        await db.Database.OpenConnectionAsync(cancellationToken);

        return new EtlContext(legacy, db, dryRun) { ObjectStore = objectStore };
    }

    public async ValueTask DisposeAsync()
    {
        await Legacy.DisposeAsync();
        await Db.DisposeAsync();
    }
}
