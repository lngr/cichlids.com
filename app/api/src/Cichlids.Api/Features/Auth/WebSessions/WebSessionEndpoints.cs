using System.Security.Cryptography;
using System.Text;
using Cichlids.Api.Features.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;

namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>
/// The state of the web app's session; CsrfToken is the token that state-changing requests send
/// in the X-CSRF-Token header, null when there is no session.
/// </summary>
public sealed record WebSessionDto(bool Authenticated, string? CsrfToken);

/// <summary>
/// The web app's sign-in through the API (backend for frontend): login start and callback of the
/// authorization code flow with PKCE, the session state and the logout. Tokens stay on the server;
/// the browser holds only the session cookie. The endpoints serve browser navigations and the web
/// app itself, so they are not part of the API description.
/// </summary>
public static class WebSessionEndpoints
{
    private const string Scopes = "openid profile email offline_access";

    public static IEndpointRouteBuilder MapWebSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").ExcludeFromDescription();
        group.MapGet("/login", LoginAsync);
        group.MapGet("/callback", CallbackAsync);
        group.MapGet("/session", SessionAsync);
        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = WebSessionAuthenticationHandler.SchemeName });
        return app;
    }

    /// <summary>Starts a login: remembers state, PKCE verifier and return address in the login cookie and redirects to Keycloak.</summary>
    private static async Task<IResult> LoginAsync(
        string? returnUrl,
        HttpResponse response,
        WebSessionSettings settings,
        KeycloakOidcClient keycloak,
        CancellationToken cancellationToken)
    {
        if (settings.CallbackUrl is null)
        {
            return ApiProblems.WithCode(StatusCodes.Status404NotFound, "Web sign-in unavailable", "This API offers no web sign-in.", "web_sign_in_disabled");
        }

        var target = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
        if (!ReturnUrls.IsAllowed(target, settings))
        {
            return ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Invalid return address", "The login can only return to the web app.", "invalid_return_url");
        }

        KeycloakEndpoints endpoints;
        try
        {
            endpoints = await keycloak.GetEndpointsAsync(cancellationToken);
        }
        catch (KeycloakUnavailableException)
        {
            return ApiProblems.WithCode(
                StatusCodes.Status503ServiceUnavailable,
                "Sign-in service unavailable",
                "The sign-in service cannot be reached. Try again later.",
                WebSessionAuthenticationHandler.IdentityProviderUnavailableCode);
        }

        var login = new PendingLogin(WebSessionService.RandomToken(), WebSessionService.RandomToken(), target);
        WebSessionCookies.WriteLogin(response, settings, login);
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(login.Verifier)));
        var location = QueryHelpers.AddQueryString(endpoints.Authorization.AbsoluteUri, new Dictionary<string, string?>
        {
            ["client_id"] = settings.ClientId,
            ["redirect_uri"] = settings.CallbackUrl.AbsoluteUri,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["state"] = login.State,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });
        return TypedResults.Redirect(location);
    }

    /// <summary>
    /// Completes a login: checks the state against the login cookie, exchanges the code with the
    /// PKCE verifier, stores the session and sets its cookie, then returns to the web app. A login
    /// that Keycloak cancelled or refused returns to the web app without a session.
    /// </summary>
    private static async Task<IResult> CallbackAsync(
        string? code,
        string? state,
        string? error,
        HttpRequest request,
        HttpResponse response,
        WebSessionSettings settings,
        KeycloakOidcClient keycloak,
        AccessTokenValidator validator,
        WebSessionService sessions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var login = WebSessionCookies.ReadLogin(request, settings);
        WebSessionCookies.ClearLogin(response, settings);
        if (settings.CallbackUrl is null
            || login is null
            || string.IsNullOrEmpty(state)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(login.State))
            || !ReturnUrls.IsAllowed(login.ReturnUrl, settings))
        {
            return ApiProblems.WithCode(
                StatusCodes.Status400BadRequest,
                "Login not recognized",
                "The login does not match a login started in this browser. Start the login again.",
                "login_state_mismatch");
        }

        var logger = loggerFactory.CreateLogger(typeof(WebSessionEndpoints));
        if (error is not null || string.IsNullOrEmpty(code))
        {
            logger.LogInformation("A web login returned from Keycloak with the error {Error}.", error);
            return TypedResults.Redirect(login.ReturnUrl);
        }

        var result = await keycloak.ExchangeCodeAsync(code, login.Verifier, settings.CallbackUrl, cancellationToken);
        if (result.Outcome != TokenRequestOutcome.Issued || string.IsNullOrEmpty(result.Tokens!.RefreshToken))
        {
            logger.LogWarning("The code of a web login could not be exchanged: {Outcome}.", result.Outcome);
            return TypedResults.Redirect(login.ReturnUrl);
        }

        var principal = await validator.ValidateAsync(result.Tokens.AccessToken, cancellationToken);
        var subject = principal?.FindFirst("sub")?.Value;
        if (subject is null)
        {
            logger.LogWarning("Keycloak issued an access token for a web login that the API does not accept.");
            return TypedResults.Redirect(login.ReturnUrl);
        }

        var sessionId = await sessions.CreateAsync(result.Tokens, subject, cancellationToken);
        WebSessionCookies.WriteSession(response, settings, sessionId);
        return TypedResults.Redirect(login.ReturnUrl);
    }

    /// <summary>
    /// The session state for the web app as it opens. Each call renews the session with Keycloak,
    /// so a suspended or deleted account reports no session, and renews the cookie's lifetime. An
    /// unreachable Keycloak leaves the session in place.
    /// </summary>
    private static async Task<Ok<WebSessionDto>> SessionAsync(
        HttpRequest request,
        HttpResponse response,
        WebSessionSettings settings,
        WebSessionService sessions,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        var sessionId = WebSessionCookies.ReadSession(request, settings);
        if (sessionId is null)
        {
            return TypedResults.Ok(new WebSessionDto(false, null));
        }

        var lookup = await sessions.FindAsync(sessionId, renew: true, cancellationToken);
        if (lookup.Session is null)
        {
            WebSessionCookies.ClearSession(response, settings);
            return TypedResults.Ok(new WebSessionDto(false, null));
        }

        await sessions.TouchAsync(lookup.Session, cancellationToken);
        WebSessionCookies.WriteSession(response, settings, sessionId);
        return TypedResults.Ok(new WebSessionDto(true, lookup.Session.CsrfToken));
    }

    /// <summary>Ends the session, revokes its offline token at Keycloak and clears the cookie.</summary>
    private static async Task<NoContent> LogoutAsync(
        HttpRequest request,
        HttpResponse response,
        WebSessionSettings settings,
        WebSessionService sessions,
        KeycloakOidcClient keycloak,
        CancellationToken cancellationToken)
    {
        var sessionId = WebSessionCookies.ReadSession(request, settings);
        var ended = sessionId is null ? null : await sessions.EndAsync(sessionId, cancellationToken);
        WebSessionCookies.ClearSession(response, settings);
        if (ended is not null && ended.RefreshToken.Length > 0)
        {
            await keycloak.EndSessionAsync(ended.RefreshToken, cancellationToken);
        }

        return TypedResults.NoContent();
    }
}
