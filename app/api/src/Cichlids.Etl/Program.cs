using Amazon.Runtime;
using Amazon.S3;
using Cichlids.Etl.Media;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "Usage: dotnet run -- <step|all|migrate|media> [--dry-run] [--out <path>]\n"
        + "  media: [--limit N] [--workers P] [--force] [--missing-out <path>]");
    return 1;
}

var command = args[0];
var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);

var outArgIndex = Array.IndexOf(args, "--out");
var verifyOutputPath = outArgIndex >= 0 && outArgIndex + 1 < args.Length ? args[outArgIndex + 1] : null;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

var legacyConnectionString =
    Environment.GetEnvironmentVariable("CICHLIDS_ETL_LEGACY_CONNECTION")
    ?? configuration["Etl:LegacyConnection"]
    ?? throw new InvalidOperationException(
        "Missing legacy connection string (Etl:LegacyConnection in appsettings.json or CICHLIDS_ETL_LEGACY_CONNECTION).");

var targetConnectionString =
    Environment.GetEnvironmentVariable("CICHLIDS_ETL_TARGET_CONNECTION")
    ?? configuration["Etl:TargetConnection"]
    ?? throw new InvalidOperationException(
        "Missing target connection string (Etl:TargetConnection in appsettings.json or CICHLIDS_ETL_TARGET_CONNECTION).");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

if (string.Equals(command, "migrate", StringComparison.OrdinalIgnoreCase))
{
    var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
        .UseNpgsql(targetConnectionString)
        .UseSnakeCaseNamingConvention();
    await using var db = new CichlidsDbContext(optionsBuilder.Options);
    await db.Database.MigrateAsync(cts.Token);
    Console.WriteLine("Migration applied.");
    return 0;
}

if (string.Equals(command, "media", StringComparison.OrdinalIgnoreCase))
{
    var legacyImagesRoot =
        Environment.GetEnvironmentVariable("CICHLIDS_ETL_LEGACY_IMAGES_ROOT")
        ?? configuration["Etl:LegacyImagesRoot"]
        ?? throw new InvalidOperationException(
            "Missing legacy images root (Etl:LegacyImagesRoot in appsettings.json or CICHLIDS_ETL_LEGACY_IMAGES_ROOT).");

    var mediaObjectStore = await RequireObjectStoreAsync(configuration, cts.Token);

    var limitArgIndex = Array.IndexOf(args, "--limit");
    var limit = limitArgIndex >= 0 && limitArgIndex + 1 < args.Length ? int.Parse(args[limitArgIndex + 1]) : (int?)null;

    var workersArgIndex = Array.IndexOf(args, "--workers");
    var workers = workersArgIndex >= 0 && workersArgIndex + 1 < args.Length ? int.Parse(args[workersArgIndex + 1]) : 8;

    var onlyMissing = !args.Contains("--force", StringComparer.OrdinalIgnoreCase);

    var missingOutArgIndex = Array.IndexOf(args, "--missing-out");
    var missingOutPath = missingOutArgIndex >= 0 && missingOutArgIndex + 1 < args.Length ? args[missingOutArgIndex + 1] : null;

    return await MediaSeedCommand.RunAsync(
        legacyConnectionString, targetConnectionString, mediaObjectStore, legacyImagesRoot,
        limit, workers, onlyMissing, missingOutPath, cts.Token);
}

IReadOnlyList<IEtlStep> steps;
if (string.Equals(command, "all", StringComparison.OrdinalIgnoreCase))
{
    steps = EtlStepRegistry.All;
}
else
{
    var step = EtlStepRegistry.Find(command);
    if (step is null)
    {
        Console.Error.WriteLine(
            $"Unknown step '{command}'. Known steps: {string.Join(", ", EtlStepRegistry.All.Select(s => s.Name))}, all, migrate.");
        return 1;
    }

    steps = [step];
}

var objectStore = await CreateObjectStoreAsync(configuration, cts.Token);

await using var context = await EtlContext.CreateAsync(
    legacyConnectionString, targetConnectionString, dryRun, cts.Token, objectStore);
context.VerifyOutputPath = verifyOutputPath;

foreach (var step in steps)
{
    Console.WriteLine($"Running step '{step.Name}'{(dryRun ? " (dry run)" : string.Empty)}...");
    await EtlRunner.RunAsync(step, context, cts.Token);
}

ConsoleReport.Print(context.Statistics, dryRun);
return context.VerificationReport is { Passed: false } ? 1 : 0;

// The media command always needs an object store to work at all, unlike the regular steps (where
// only the forum step does), so it fails up front with a clear message instead of a
// null-reference deeper in the command.
static async Task<IObjectStore> RequireObjectStoreAsync(IConfiguration configuration, CancellationToken cancellationToken)
{
    return await CreateObjectStoreAsync(configuration, cancellationToken)
        ?? throw new InvalidOperationException(
            "This command requires an object store; configure the \"ObjectStorage\" section before running it.");
}

// Builds the object store steps use to export legacy binary content (for example forum
// attachments), and ensures its bucket exists. Only steps that actually need it (checked through
// EtlContext.ObjectStore being null) fail when the "ObjectStorage" section is absent, so unrelated
// steps and older configuration keep working without it.
static async Task<IObjectStore?> CreateObjectStoreAsync(IConfiguration configuration, CancellationToken cancellationToken)
{
    var section = configuration.GetSection(S3ObjectStoreOptions.SectionName);
    if (!section.Exists())
    {
        return null;
    }

    var options = new S3ObjectStoreOptions
    {
        ServiceUrl = section["ServiceUrl"] ?? string.Empty,
        Region = section["Region"] ?? string.Empty,
        Bucket = section["Bucket"] ?? string.Empty,
        AccessKey = section["AccessKey"] ?? string.Empty,
        SecretKey = section["SecretKey"] ?? string.Empty,
        ForcePathStyle = bool.TryParse(section["ForcePathStyle"], out var forcePathStyle) && forcePathStyle,
        PublicBaseUrl = section["PublicBaseUrl"] ?? string.Empty,
    };

    var s3Config = new AmazonS3Config
    {
        ServiceURL = options.ServiceUrl,
        ForcePathStyle = options.ForcePathStyle,
        AuthenticationRegion = options.Region,
    };
    var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
    var client = new AmazonS3Client(credentials, s3Config);
    var store = new S3ObjectStore(client, Options.Create(options));

    await store.EnsureBucketAsync(cancellationToken);
    return store;
}
