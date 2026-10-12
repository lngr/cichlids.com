namespace Cichlids.Api.Features.Auth;

/// <summary>
/// The validated token authentication settings read from the Authentication configuration
/// section. Outside the Development environment the authority must be an absolute HTTPS URL, the
/// audience must be set and metadata must be fetched over HTTPS, so a misconfigured deployment
/// fails at startup.
/// </summary>
/// <param name="Authority">The issuer URL whose published keys validate incoming tokens.</param>
/// <param name="Audience">The audience value a token must contain.</param>
/// <param name="RequireHttpsMetadata">Whether the issuer metadata must be fetched over HTTPS.</param>
public sealed record AuthenticationSettings(string? Authority, string? Audience, bool RequireHttpsMetadata)
{
    private const string AuthorityKey = "Authentication:Authority";
    private const string AudienceKey = "Authentication:Audience";
    private const string RequireHttpsMetadataKey = "Authentication:RequireHttpsMetadata";

    /// <summary>
    /// Reads the settings for the given host environment name. In the Development environment
    /// the values are taken as configured. Outside Development, an authority that is missing or
    /// not an absolute HTTPS URL, a blank audience or disabled HTTPS metadata raises an exception
    /// naming the configuration key to fix.
    /// </summary>
    public static AuthenticationSettings Resolve(IConfiguration configuration, string? environmentName)
    {
        var authority = configuration[AuthorityKey];
        var audience = configuration[AudienceKey];
        var requireHttpsMetadata = configuration.GetValue(RequireHttpsMetadataKey, true);

        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        if (isDevelopment)
        {
            return new AuthenticationSettings(authority, audience, requireHttpsMetadata);
        }

        if (string.IsNullOrWhiteSpace(authority))
        {
            throw Refuse(environmentName, $"{AuthorityKey} is not set. Set it to the HTTPS URL of the identity provider realm.");
        }

        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) || authorityUri.Scheme != Uri.UriSchemeHttps)
        {
            throw Refuse(environmentName, $"{AuthorityKey} must be an absolute HTTPS URL but is '{authority}'.");
        }

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw Refuse(environmentName, $"{AudienceKey} is not set. Set it to the audience the API tokens carry.");
        }

        if (!requireHttpsMetadata)
        {
            throw Refuse(environmentName, $"{RequireHttpsMetadataKey} must not be false; only the Development environment may fetch issuer metadata over plain HTTP.");
        }

        return new AuthenticationSettings(authority, audience, requireHttpsMetadata);
    }

    private static InvalidOperationException Refuse(string? environmentName, string reason) =>
        new($"Invalid authentication configuration for environment '{environmentName}': {reason}");
}
