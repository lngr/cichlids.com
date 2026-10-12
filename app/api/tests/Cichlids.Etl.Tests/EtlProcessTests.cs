// Story: task-3.30
using System.Diagnostics;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Starts the built ETL entry point as its own process. The command line is parsed before any
/// configuration or connection is used, so these runs need no database; the connection settings
/// point at a closed port so a run that got past parsing would fail without reaching a real
/// database.
/// </summary>
public sealed class EtlProcessTests
{
    private const string UnreachableTarget = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1";
    private const string UnreachableLegacy = "Server=127.0.0.1;Port=1;Database=none;User=none;Password=none;Connection Timeout=1";

    [Fact]
    public async Task RejectsTheDryRunOptionBeforeTouchingAnyDatabase()
    {
        var (exitCode, _, standardError) = await RunEtlAsync("Development", "all", "--dry-run");

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option '--dry-run'", standardError);
    }

    [Fact]
    public async Task RejectsASingleWriteStepInStagingAndNamesTheEnvironment()
    {
        var (exitCode, _, standardError) = await RunEtlAsync("Staging", "profiles");

        Assert.Equal(1, exitCode);
        Assert.Contains("'Staging'", standardError);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunEtlAsync(
        string environmentName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Cichlids.Etl.dll"));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_ENVIRONMENT"] = environmentName;
        startInfo.Environment["CICHLIDS_ETL_TARGET_CONNECTION"] = UnreachableTarget;
        startInfo.Environment["CICHLIDS_ETL_LEGACY_CONNECTION"] = UnreachableLegacy;
        startInfo.Environment["CICHLIDS_KEYCLOAK_URL"] = "http://127.0.0.1:1";

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await process.WaitForExitAsync(timeout.Token);

        return (process.ExitCode, await standardOutput, await standardError);
    }
}
