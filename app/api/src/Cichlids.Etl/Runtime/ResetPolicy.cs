namespace Cichlids.Etl.Runtime;

/// <summary>
/// Decides where the reset may run: in the Development and Staging environments, matched without
/// regard to case. Every other environment name is refused, an empty or missing one included,
/// because an unset environment counts as Production.
/// </summary>
public static class ResetPolicy
{
    private static readonly string[] AllowedEnvironments = ["Development", "Staging"];

    public static bool Allows(string? environmentName) =>
        environmentName is not null
        && AllowedEnvironments.Contains(environmentName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Throws a ResetRefusedException naming the environment when the reset may not run there.
    /// </summary>
    public static void EnsureAllowed(string? environmentName)
    {
        if (!Allows(environmentName))
        {
            throw new ResetRefusedException(
                $"The reset refuses to run in the environment '{environmentName}'; it runs only in "
                + $"{string.Join(" and ", AllowedEnvironments)}. Nothing was changed.");
        }
    }
}

/// <summary>
/// Thrown when the reset is invoked in an environment the reset policy refuses.
/// </summary>
public sealed class ResetRefusedException(string message) : InvalidOperationException(message);
