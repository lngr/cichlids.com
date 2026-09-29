using Microsoft.Extensions.Configuration;

namespace Cichlids.Infrastructure.Slugs;

/// <summary>
/// Resolves the slug secret that keys picture slugs, generated user handles, login usernames and
/// guest names, reading the CICHLIDS_SLUG_SECRET environment variable before the Slugs:Secret
/// configuration key. Every value minted under a given secret only ever matches the same input
/// again as long as that secret stays the same, so a deployment outside the Development
/// environment must never run with the well-known local development secret or with none at all;
/// doing so would let two different deployments mint colliding or silently different values for
/// the same legacy row.
/// </summary>
public static class SlugSecretResolver
{
    /// <summary>
    /// The secret every local development configuration and fixture uses. Allowed only in the
    /// Development environment.
    /// </summary>
    public const string DevelopmentDefault = "cichlids-local-dev-slug-secret"; // gitleaks:allow

    private const string DevelopmentEnvironmentName = "Development";

    /// <summary>
    /// Returns the secret to use for the given host or runtime environment name. In the
    /// Development environment a missing value resolves to the development default and any
    /// configured value is accepted. Outside Development, a missing value or the development
    /// default raises an exception naming the environment variable and configuration key to set.
    /// </summary>
    public static string Resolve(IConfiguration configuration, string? environmentName)
    {
        var configured = Environment.GetEnvironmentVariable(SlugGenerator.SecretEnvironmentVariable);
        if (string.IsNullOrEmpty(configured))
        {
            configured = configuration[SlugGenerator.SecretConfigurationKey];
        }

        var isDevelopment = string.Equals(environmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase);
        if (isDevelopment)
        {
            return string.IsNullOrEmpty(configured) ? DevelopmentDefault : configured;
        }

        if (string.IsNullOrEmpty(configured) || configured == DevelopmentDefault)
        {
            throw new InvalidOperationException(
                $"No slug secret configured for environment '{environmentName}'. Slugs, generated "
                + $"handles, login usernames and guest names must stay stable, so this environment "
                + $"refuses to start without one. Set {SlugGenerator.SecretEnvironmentVariable} (or "
                + $"{SlugGenerator.SecretConfigurationKey}) to a secret of its own; the local "
                + "development default is only allowed in the Development environment.");
        }

        return configured;
    }
}
