// Story: task-3.30
using System.Diagnostics;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Starts the built ETL entry point as its own process. The command line and the reset policy are
/// checked before any connection is opened, so these runs need no database; the connection
/// settings point at a closed port so a run that got past those checks would fail without reaching
/// a real database or Keycloak.
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

    [Theory]
    [InlineData("Production")]
    [InlineData(null)]
    public async Task RefusesTheResetOutsideDevelopmentAndStaging(string? environmentName)
    {
        var (exitCode, _, standardError) = await RunEtlAsync(environmentName, "reset");

        Assert.Equal(1, exitCode);
        Assert.Contains("The reset refuses to run in the environment 'Production'", standardError);
    }

    [Fact]
    public async Task RefusesTheKeycloakImportOutsideDevelopmentWithoutAdminCredentials()
    {
        var (exitCode, _, standardError) = await RunEtlAsync("Staging", "keycloak-import");

        Assert.Equal(1, exitCode);
        Assert.Contains("CICHLIDS_KEYCLOAK_ADMIN_PASSWORD", standardError);
        Assert.Contains("Keycloak:AdminPassword", standardError);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunEtlAsync(
        string? environmentName, params string[] arguments)
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

        if (environmentName is null)
        {
            startInfo.Environment.Remove("DOTNET_ENVIRONMENT");
        }
        else
        {
            startInfo.Environment["DOTNET_ENVIRONMENT"] = environmentName;
        }

        startInfo.Environment["CICHLIDS_ETL_TARGET_CONNECTION"] = UnreachableTarget;
        startInfo.Environment["CICHLIDS_ETL_LEGACY_CONNECTION"] = UnreachableLegacy;
        startInfo.Environment["CICHLIDS_KEYCLOAK_URL"] = "http://127.0.0.1:1";
        startInfo.Environment.Remove("CICHLIDS_KEYCLOAK_ADMIN_USER");
        startInfo.Environment.Remove("CICHLIDS_KEYCLOAK_ADMIN_PASSWORD");

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await process.WaitForExitAsync(timeout.Token);

        return (process.ExitCode, await standardOutput, await standardError);
    }
}
