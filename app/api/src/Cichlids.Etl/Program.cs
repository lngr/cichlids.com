using System.ComponentModel.DataAnnotations;
using Amazon.Runtime;
using Amazon.S3;
using Cichlids.Etl.Identity;
using Cichlids.Etl.Media;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Slugs;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

// DOTNET_ENVIRONMENT is the generic host's own environment variable; unset resolves to
// Production, its documented default, since this entry point has no host builder to apply that
// default for it.
var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

var parsed = EtlCommandLine.Parse(args, environmentName);
if (parsed.CommandLine is not { } commandLine)
{
    Console.Error.WriteLine(parsed.Error);
    return 1;
}

var command = commandLine.Command;

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

if (command == EtlCommandLine.Migrate)
{
    var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
        .UseNpgsql(targetConnectionString)
        .UseSnakeCaseNamingConvention();
    await using var db = new CichlidsDbContext(optionsBuilder.Options);
    await db.Database.MigrateAsync(cts.Token);
    Console.WriteLine("Migration applied.");
    return 0;
}

if (command == EtlCommandLine.Reset)
{
    return await ResetCommand.RunAsync(
        targetConnectionString, () => KeycloakAdminSettings.Resolve(configuration, environmentName), environmentName, cts.Token);
}

if (command == EtlCommandLine.Media)
{
    var legacyImagesRoot =
        Environment.GetEnvironmentVariable("CICHLIDS_ETL_LEGACY_IMAGES_ROOT")
        ?? configuration["Etl:LegacyImagesRoot"]
        ?? throw new InvalidOperationException(
            "Missing legacy images root (Etl:LegacyImagesRoot in appsettings.json or CICHLIDS_ETL_LEGACY_IMAGES_ROOT).");

    var mediaObjectStore = await RequireObjectStoreAsync(configuration, cts.Token);

    // Legacy files are held to the same size limit as object downloads.
    var maxSourceBytes = configuration.GetSection(S3ObjectStoreOptions.SectionName).Get<S3ObjectStoreOptions>()!.MaxDownloadBytes;

    return await MediaSeedCommand.RunAsync(
        legacyConnectionString, targetConnectionString, mediaObjectStore, legacyImagesRoot,
        commandLine.Limit, commandLine.Workers, commandLine.MissingOutPath, maxSourceBytes, cts.Token);
}

if (command == EtlCommandLine.MediaVerify)
{
    var verifyObjectStore = await RequireObjectStoreAsync(configuration, cts.Token);
    return await MediaVerifyCommand.RunAsync(targetConnectionString, verifyObjectStore, commandLine.OutPath, cts.Token);
}

if (command == EtlCommandLine.KeycloakImport)
{
    var auth0ExportPath =
        Environment.GetEnvironmentVariable("CICHLIDS_ETL_AUTH0_EXPORT")
        ?? configuration["Etl:Auth0ExportPath"]
        ?? "/workspaces/legacy-data/auth0/auth0-cichlids.json";

    KeycloakAdminSettings keycloakSettings;
    try
    {
        keycloakSettings = KeycloakAdminSettings.Resolve(configuration, environmentName);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    var keycloakSlugSecret = SlugSecretResolver.Resolve(configuration, environmentName);
    return await KeycloakAccountImportCommand.RunAsync(
        legacyConnectionString, targetConnectionString, auth0ExportPath, keycloakSettings, new GeneratedNames(keycloakSlugSecret), commandLine.OpenRegistration, cts.Token);
}

var slugSecret = SlugSecretResolver.Resolve(configuration, environmentName);

await using var context = await EtlContext.CreateAsync(
    legacyConnectionString, targetConnectionString, cts.Token, slugSecret: slugSecret);
context.VerifyOutputPath = commandLine.OutPath;

if (command == EtlCommandLine.All)
{
    int exitCode;
    try
    {
        // The object store is prepared once the guard has passed, since preparing it writes the
        // bucket and its policy.
        exitCode = await EtlRunner.RunAllAsync(
            context,
            EtlStepRegistry.WriteSteps,
            EtlStepRegistry.Verify,
            async cancellationToken => context.ObjectStore = await CreateObjectStoreAsync(configuration, cancellationToken),
            cts.Token);
    }
    catch (EmptyTargetException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    ConsoleReport.Print(context.Statistics);
    return exitCode;
}

// The verify command only reads; a single write step can need the object store.
if (commandLine.IsSingleStep)
{
    Console.WriteLine(
        $"Warning: the single step '{command}' runs without the empty-target guard and writes over the rows it finds.");
    context.ObjectStore = await CreateObjectStoreAsync(configuration, cts.Token);
}

var step = EtlStepRegistry.Find(command)!;
Console.WriteLine($"Running step '{step.Name}'...");
await EtlRunner.RunAsync(step, context, cts.Token);

ConsoleReport.Print(context.Statistics);
return context.VerificationReport is { Passed: false } ? 1 : 0;

// The media and media-verify commands always need an object store to work at all, unlike the
// regular steps (where only the forum step does), so they fail up front with a clear message
// instead of a null-reference deeper in the command.
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

    var options = section.Get<S3ObjectStoreOptions>() ?? new S3ObjectStoreOptions();
    Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);

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

    var policyApplied = await store.EnsurePublicReadPolicyAsync(MediaStorageKeys.PublicPrefixes, cancellationToken);
    if (!policyApplied)
    {
        Console.Error.WriteLine(
            $"Warning: the object store provider does not support PutBucketPolicy; bucket '{options.Bucket}' "
            + "was not configured for public read of variants and forum attachments. Served media may return 403 until this is configured another way.");
    }

    return store;
}
