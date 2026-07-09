using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Slugs;
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
    /// <summary>
    /// Secret the local dev stack's Slugs:Secret configuration also defaults to; used here only
    /// when a caller creates a context without resolving its own secret (tests, and any command
    /// that never touches a generated slug), so a real deployment secret is never silently
    /// guessed.
    /// </summary>
    public const string DevDefaultSlugSecret = "cichlids-local-dev-slug-secret";

    private EtlContext(MySqlConnection legacy, CichlidsDbContext db, bool dryRun, SlugGenerator slugGenerator)
    {
        Legacy = legacy;
        Db = db;
        DryRun = dryRun;
        SlugGenerator = slugGenerator;
    }

    public MySqlConnection Legacy { get; }

    public CichlidsDbContext Db { get; }

    public NpgsqlConnection Target => (NpgsqlConnection)Db.Database.GetDbConnection();

    public bool DryRun { get; }

    public EtlStatistics Statistics { get; } = new();

    /// <summary>
    /// Derives short generated slugs for posts with no surviving legacy alias, keyed on the
    /// secret this context was created with, falling back to DevDefaultSlugSecret above when the
    /// caller passes none.
    /// </summary>
    public SlugGenerator SlugGenerator { get; }

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

    /// <summary>
    /// Destination path for the verification step's JSON report, set from the command line's
    /// "--out" option. Null when no path was given, in which case the step only prints its console
    /// table.
    /// </summary>
    public string? VerifyOutputPath { get; set; }

    /// <summary>
    /// The verification step's outcome, set once it has run, so the entry point can turn a
    /// mismatch into a non-zero exit code without every other step needing to know verification
    /// exists.
    /// </summary>
    public VerificationReport? VerificationReport { get; set; }

    public static async Task<EtlContext> CreateAsync(
        string legacyConnectionString,
        string targetConnectionString,
        bool dryRun,
        CancellationToken cancellationToken,
        IObjectStore? objectStore = null,
        string? slugSecret = null)
    {
        var legacy = new MySqlConnection(legacyConnectionString);
        await legacy.OpenAsync(cancellationToken);

        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(targetConnectionString)
            .UseSnakeCaseNamingConvention();
        var db = new CichlidsDbContext(optionsBuilder.Options);
        await db.Database.OpenConnectionAsync(cancellationToken);

        var slugGenerator = new SlugGenerator(slugSecret ?? DevDefaultSlugSecret);
        return new EtlContext(legacy, db, dryRun, slugGenerator) { ObjectStore = objectStore };
    }

    public async ValueTask DisposeAsync()
    {
        await Legacy.DisposeAsync();
        await Db.DisposeAsync();
    }
}
