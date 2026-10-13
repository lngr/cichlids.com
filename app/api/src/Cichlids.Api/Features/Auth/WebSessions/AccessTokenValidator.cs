using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>
/// Validates an access token of a web session with the settings of the bearer token scheme
/// (issuer keys, audience, lifetime, claim names) and promotes its Keycloak realm roles, so a web
/// session yields the same principal as the same token sent as a bearer token.
/// </summary>
public sealed class AccessTokenValidator(IOptionsMonitor<JwtBearerOptions> optionsMonitor)
{
    /// <summary>The principal of the token, or null when the token is not valid.</summary>
    public async Task<ClaimsPrincipal?> ValidateAsync(string accessToken, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);
        var parameters = options.TokenValidationParameters.Clone();
        if (options.ConfigurationManager is BaseConfigurationManager configurationManager)
        {
            parameters.ConfigurationManager = configurationManager;
        }
        else if (options.ConfigurationManager is not null)
        {
            var configuration = await options.ConfigurationManager.GetConfigurationAsync(cancellationToken);
            parameters.ValidIssuers = [.. parameters.ValidIssuers ?? [], configuration.Issuer];
            parameters.IssuerSigningKeys = [.. parameters.IssuerSigningKeys ?? [], .. configuration.SigningKeys];
        }

        foreach (var handler in options.TokenHandlers)
        {
            var result = await handler.ValidateTokenAsync(accessToken, parameters);
            if (result.IsValid && result.ClaimsIdentity is { } identity)
            {
                KeycloakRealmRoles.Promote(identity);
                return new ClaimsPrincipal(identity);
            }
        }

        return null;
    }
}
