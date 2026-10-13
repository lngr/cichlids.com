using Microsoft.AspNetCore.DataProtection;

namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>
/// The validated location of the ASP.NET Core Data Protection key ring from the configuration key
/// DataProtection:KeyRingPath. Outside the Development environment the directory must exist and
/// the API only reads it: the key ring comes from a secret mounted read-only, so a deployment
/// without it refuses to start. Development creates its keys in the configured directory, or in
/// the default Data Protection location when none is configured; a relative path is resolved
/// against the content root.
/// </summary>
/// <param name="Path">The absolute key ring directory, or null for the default location.</param>
/// <param name="CreatesKeys">Whether the API creates and renews keys in the key ring.</param>
public sealed record KeyRingSettings(string? Path, bool CreatesKeys)
{
    /// <summary>
    /// The application name of the key ring. Every instance of the API shares it, so each one
    /// reads what another one encrypted, wherever it is installed.
    /// </summary>
    public const string ApplicationName = "cichlids-api";

    private const string PathKey = "DataProtection:KeyRingPath";

    /// <summary>
    /// Reads the settings for the given host environment name and throws an exception naming the
    /// configuration key when the key ring is missing outside Development.
    /// </summary>
    public static KeyRingSettings Resolve(IConfiguration configuration, string? environmentName, string contentRootPath)
    {
        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        var configured = configuration[PathKey];
        var path = string.IsNullOrWhiteSpace(configured)
            ? null
            : System.IO.Path.GetFullPath(configured, contentRootPath);

        if (isDevelopment)
        {
            return new KeyRingSettings(path, CreatesKeys: true);
        }

        if (path is null)
        {
            throw Refuse(environmentName, $"{PathKey} is not set. Set it to the directory of the Data Protection key ring that encrypts the web session tokens.");
        }

        if (!Directory.Exists(path))
        {
            throw Refuse(environmentName, $"{PathKey} names the directory '{path}', which does not exist.");
        }

        return new KeyRingSettings(path, CreatesKeys: false);
    }

    private static InvalidOperationException Refuse(string? environmentName, string reason) =>
        new($"Invalid Data Protection configuration for environment '{environmentName}': {reason}");
}

/// <summary>Registers ASP.NET Core Data Protection with the key ring of the settings.</summary>
public static class KeyRingServiceCollectionExtensions
{
    public static IServiceCollection AddKeyRing(this IServiceCollection services, KeyRingSettings settings)
    {
        var builder = services.AddDataProtection().SetApplicationName(KeyRingSettings.ApplicationName);
        if (settings.Path is not null)
        {
            builder.PersistKeysToFileSystem(new DirectoryInfo(settings.Path));
        }

        if (!settings.CreatesKeys)
        {
            // The operator provides the keys; encryption needs one that has not expired.
            builder.DisableAutomaticKeyGeneration();
        }

        return services;
    }
}
