// Story: task-3.30
using Cichlids.Etl.Runtime;

namespace Cichlids.Etl.Tests;

public sealed class EtlCommandLineTests
{
    [Fact]
    public void RejectsTheDryRunOptionAsUnknown()
    {
        var result = EtlCommandLine.Parse(["all", "--dry-run"], "Development");

        Assert.Null(result.CommandLine);
        Assert.Contains("Unknown option '--dry-run'", result.Error);
    }

    [Fact]
    public void AcceptsASingleWriteStepInDevelopment()
    {
        var result = EtlCommandLine.Parse(["profiles"], "Development");

        Assert.Null(result.Error);
        Assert.Equal("profiles", result.CommandLine!.Command);
        Assert.True(result.CommandLine.IsSingleStep);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void RejectsASingleWriteStepOutsideDevelopmentAndNamesTheEnvironment(string environmentName)
    {
        var result = EtlCommandLine.Parse(["profiles"], environmentName);

        Assert.Null(result.CommandLine);
        Assert.Contains("'profiles'", result.Error);
        Assert.Contains($"'{environmentName}'", result.Error);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("")]
    public void ParsesTheResetCommandInEveryEnvironment(string environmentName)
    {
        var result = EtlCommandLine.Parse(["reset"], environmentName);

        Assert.Null(result.Error);
        Assert.Equal("reset", result.CommandLine!.Command);
        Assert.False(result.CommandLine.IsSingleStep);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("verify")]
    [InlineData("migrate")]
    [InlineData("media")]
    [InlineData("media-verify")]
    [InlineData("keycloak-import")]
    public void AcceptsTheGuardedAndReadOnlyCommandsInProduction(string command)
    {
        var result = EtlCommandLine.Parse([command], "Production");

        Assert.Null(result.Error);
        Assert.Equal(command, result.CommandLine!.Command);
    }

    [Fact]
    public void MatchesTheCommandNameIgnoringCase()
    {
        var result = EtlCommandLine.Parse(["ALL"], "Production");

        Assert.Equal("all", result.CommandLine!.Command);
    }

    [Fact]
    public void ParsesTheMediaOptions()
    {
        var result = EtlCommandLine.Parse(
            ["media", "--limit", "25", "--workers", "4", "--missing-out", "missing.tsv"], "Development");

        Assert.Null(result.Error);
        var commandLine = result.CommandLine!;
        Assert.Equal(25, commandLine.Limit);
        Assert.Equal(4, commandLine.Workers);
        Assert.Equal("missing.tsv", commandLine.MissingOutPath);
    }

    [Fact]
    public void AppliesTheMediaDefaultsWhenNoOptionIsGiven()
    {
        var commandLine = EtlCommandLine.Parse(["media"], "Development").CommandLine!;

        Assert.Null(commandLine.Limit);
        Assert.Equal(EtlCommandLine.DefaultWorkers, commandLine.Workers);
        Assert.Null(commandLine.MissingOutPath);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("verify")]
    [InlineData("media-verify")]
    public void ParsesTheReportPath(string command)
    {
        var result = EtlCommandLine.Parse([command, "--out", "report.json"], "Production");

        Assert.Null(result.Error);
        Assert.Equal("report.json", result.CommandLine!.OutPath);
    }

    [Fact]
    public void ParsesTheOpenRegistrationFlag()
    {
        var result = EtlCommandLine.Parse(["keycloak-import", "--open-registration"], "Production");

        Assert.Null(result.Error);
        Assert.True(result.CommandLine!.OpenRegistration);
    }

    [Theory]
    [InlineData("all", "--out")]
    [InlineData("media", "--limit")]
    [InlineData("media", "--missing-out")]
    public void RejectsAnOptionWithoutItsValue(string command, string option)
    {
        var result = EtlCommandLine.Parse([command, option], "Development");

        Assert.Null(result.CommandLine);
        Assert.Contains($"Option '{option}' needs a value", result.Error);
    }

    [Fact]
    public void RejectsAnOptionFollowedByAnotherOptionInPlaceOfItsValue()
    {
        var result = EtlCommandLine.Parse(["media", "--missing-out", "--limit"], "Development");

        Assert.Contains("Option '--missing-out' needs a value", result.Error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    public void RejectsANumericOptionThatIsNoPositiveWholeNumber(string value)
    {
        var result = EtlCommandLine.Parse(["media", "--workers", value], "Development");

        Assert.Null(result.CommandLine);
        Assert.Contains("'--workers'", result.Error);
    }

    [Theory]
    [InlineData("reset", "--out")]
    [InlineData("migrate", "--force")]
    [InlineData("media", "--force")]
    [InlineData("profiles", "--out")]
    [InlineData("all", "--open-registration")]
    public void RejectsAnOptionTheCommandDoesNotTake(string command, string option)
    {
        var result = EtlCommandLine.Parse([command, option, "x"], "Development");

        Assert.Null(result.CommandLine);
        Assert.Contains($"Unknown option '{option}'", result.Error);
    }

    [Fact]
    public void RejectsARepeatedOption()
    {
        var result = EtlCommandLine.Parse(["all", "--out", "a.json", "--out", "b.json"], "Development");

        Assert.Null(result.CommandLine);
        Assert.Contains("'--out'", result.Error);
    }

    [Fact]
    public void RejectsAnUnknownCommand()
    {
        var result = EtlCommandLine.Parse(["frobnicate"], "Development");

        Assert.Null(result.CommandLine);
        Assert.Contains("Unknown command 'frobnicate'", result.Error);
    }

    [Fact]
    public void RejectsAMissingCommand()
    {
        var result = EtlCommandLine.Parse([], "Development");

        Assert.Null(result.CommandLine);
        Assert.Contains("Usage", result.Error);
    }
}
