using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run -- <step|all|migrate> [--dry-run]");
    return 1;
}

var command = args[0];
var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);

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

await using var context = await EtlContext.CreateAsync(legacyConnectionString, targetConnectionString, dryRun, cts.Token);

foreach (var step in steps)
{
    Console.WriteLine($"Running step '{step.Name}'{(dryRun ? " (dry run)" : string.Empty)}...");
    await EtlRunner.RunAsync(step, context, cts.Token);
}

ConsoleReport.Print(context.Statistics, dryRun);
return 0;
