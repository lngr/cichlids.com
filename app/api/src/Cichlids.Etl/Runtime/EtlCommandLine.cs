using Cichlids.Etl.Steps;

namespace Cichlids.Etl.Runtime;

/// <summary>
/// The parsed ETL command line: one command and the options that command takes. Parsing is strict.
/// An unknown command, an option the command does not take, a missing or malformed option value
/// and a repeated option are errors. The guarded full run, the read-only checks, the schema
/// migration, the reset, the media commands and the account import are known in every
/// environment. A single write step skips the empty-target guard and is accepted only in the
/// Development environment.
/// </summary>
public sealed record EtlCommandLine
{
    /// <summary>
    /// The number of parallel media workers when the command line names none.
    /// </summary>
    public const int DefaultWorkers = 8;

    public const string All = "all";
    public const string Verify = "verify";
    public const string Migrate = "migrate";
    public const string Reset = "reset";
    public const string Media = "media";
    public const string MediaVerify = "media-verify";
    public const string KeycloakImport = "keycloak-import";

    private const string OutOption = "--out";
    private const string LimitOption = "--limit";
    private const string WorkersOption = "--workers";
    private const string ForceOption = "--force";
    private const string MissingOutOption = "--missing-out";
    private const string OpenRegistrationOption = "--open-registration";

    private const string DevelopmentEnvironment = "Development";

    private static readonly string[] ValueOptions = [OutOption, LimitOption, WorkersOption, MissingOutOption];

    private static readonly Dictionary<string, string[]> OptionsByCommand = new(StringComparer.Ordinal)
    {
        [All] = [OutOption],
        [Verify] = [OutOption],
        [Migrate] = [],
        [Reset] = [],
        [Media] = [LimitOption, WorkersOption, ForceOption, MissingOutOption],
        [MediaVerify] = [OutOption],
        [KeycloakImport] = [OpenRegistrationOption],
    };

    /// <summary>
    /// The usage text printed with a command line error.
    /// </summary>
    public static string Usage =>
        "Usage: dotnet run -- <command> [options]\n"
        + "  all [--out <path>]        guarded full run into an empty target, then verify\n"
        + "  verify [--out <path>]     compares the target with the legacy source (read-only)\n"
        + "  migrate                   applies the schema migrations\n"
        + "  reset                     empties the target and the realm users (Development and Staging only)\n"
        + "  media [--limit N] [--workers P] [--force] [--missing-out <path>]\n"
        + "  media-verify [--out <path>]\n"
        + "  keycloak-import [--open-registration]\n"
        + "                            imports the Auth0 export (Etl:Auth0ExportPath) into Keycloak (Keycloak:*);\n"
        + "                            --open-registration allows self-registration once every account exists\n"
        + $"  <step>                    one write step without the empty-target guard (Development only): {string.Join(", ", WriteStepNames)}";

    private static IEnumerable<string> WriteStepNames =>
        EtlStepRegistry.All.Select(step => step.Name).Where(name => name != Verify);

    /// <summary>
    /// The command in lower case.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// True when the command is a single write step of the step registry.
    /// </summary>
    public bool IsSingleStep { get; init; }

    /// <summary>
    /// The report path from "--out", or null when none is given.
    /// </summary>
    public string? OutPath { get; init; }

    /// <summary>
    /// The maximum number of media items from "--limit", or null for no limit.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// The number of parallel media workers from "--workers".
    /// </summary>
    public int Workers { get; init; } = DefaultWorkers;

    /// <summary>
    /// True when "--force" is given.
    /// </summary>
    public bool Force { get; init; }

    /// <summary>
    /// The path for the list of missing media files from "--missing-out", or null when none is
    /// given.
    /// </summary>
    public string? MissingOutPath { get; init; }

    /// <summary>
    /// True when "--open-registration" is given.
    /// </summary>
    public bool OpenRegistration { get; init; }

    /// <summary>
    /// Parses the arguments for the given environment name. Returns either the command line or an
    /// error message.
    /// </summary>
    public static EtlCommandLineParseResult Parse(IReadOnlyList<string> args, string environmentName)
    {
        if (args.Count == 0)
        {
            return Fail("No command given.");
        }

        var command = args[0].ToLowerInvariant();
        string[] allowedOptions;
        var isSingleStep = false;
        if (OptionsByCommand.TryGetValue(command, out var commandOptions))
        {
            allowedOptions = commandOptions;
        }
        else if (WriteStepNames.Contains(command))
        {
            if (!string.Equals(environmentName, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase))
            {
                return Fail(
                    $"The single step '{command}' runs only in the Development environment; the environment is '{environmentName}'. "
                    + $"Use '{All}' for the guarded full run.");
            }

            allowedOptions = [];
            isSingleStep = true;
        }
        else
        {
            return Fail($"Unknown command '{args[0]}'.");
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 1; i < args.Count; i++)
        {
            var option = args[i];
            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                return Fail($"Unexpected argument '{option}' for '{command}'.");
            }

            if (!allowedOptions.Contains(option))
            {
                var known = allowedOptions.Length == 0 ? "none" : string.Join(", ", allowedOptions);
                return Fail($"Unknown option '{option}' for '{command}'. Options of '{command}': {known}.");
            }

            if (values.ContainsKey(option))
            {
                return Fail($"Option '{option}' is given more than once.");
            }

            string? value = null;
            if (ValueOptions.Contains(option))
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return Fail($"Option '{option}' needs a value.");
                }

                value = args[++i];
            }

            values[option] = value;
        }

        int? limit = null;
        if (values.TryGetValue(LimitOption, out var limitValue))
        {
            if (!TryParsePositive(limitValue!, out var parsedLimit))
            {
                return Fail($"Option '{LimitOption}' needs a positive whole number, got '{limitValue}'.");
            }

            limit = parsedLimit;
        }

        var workers = DefaultWorkers;
        if (values.TryGetValue(WorkersOption, out var workersValue) && !TryParsePositive(workersValue!, out workers))
        {
            return Fail($"Option '{WorkersOption}' needs a positive whole number, got '{workersValue}'.");
        }

        return new EtlCommandLineParseResult(
            new EtlCommandLine
            {
                Command = command,
                IsSingleStep = isSingleStep,
                OutPath = values.GetValueOrDefault(OutOption),
                Limit = limit,
                Workers = workers,
                Force = values.ContainsKey(ForceOption),
                MissingOutPath = values.GetValueOrDefault(MissingOutOption),
                OpenRegistration = values.ContainsKey(OpenRegistrationOption),
            },
            null);
    }

    private static bool TryParsePositive(string value, out int result) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out result)
        && result > 0;

    private static EtlCommandLineParseResult Fail(string message) => new(null, $"{message}\n{Usage}");
}

/// <summary>
/// The outcome of parsing an ETL command line: the command line when it is valid, otherwise the
/// error message with the usage text.
/// </summary>
public sealed record EtlCommandLineParseResult(EtlCommandLine? CommandLine, string? Error);
