using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>
/// Authenticates a request of the web app through its session cookie. The principal comes from
/// the session's access token, renewed first when it is about to expire. A state-changing request
/// must send the session's CSRF token in the X-CSRF-Token header, and its Origin, when present,
/// must be an allowed web app origin. A challenge answers 403 with the code csrf_rejected for a
/// failed CSRF check, 503 with the code identity_provider_unavailable when the access token could
/// not be renewed, and 401 otherwise; for a missing or ended session it also clears the cookie.
/// </summary>
public sealed class WebSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    WebSessionService sessions,
    WebSessionSettings settings,
    AccessTokenValidator validator,
    IProblemDetailsService problemDetails)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "WebSession";
    public const string CsrfHeader = "X-CSRF-Token";
    public const string CsrfRejectedCode = "csrf_rejected";
    public const string IdentityProviderUnavailableCode = "identity_provider_unavailable";

    private enum Failure
    {
        None,
        Csrf,
        Unavailable,
    }

    private Failure _failure;

    /// <summary>
    /// Whether a request authenticates through the web session: it has a session cookie and no
    /// Authorization header, since a bearer token always takes precedence.
    /// </summary>
    public static bool Applies(HttpContext context, WebSessionSettings settings) =>
        !context.Request.Headers.ContainsKey("Authorization")
        && WebSessionCookies.ReadSession(context.Request, settings) is not null;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sessionId = WebSessionCookies.ReadSession(Request, settings);
        if (sessionId is null)
        {
            return AuthenticateResult.NoResult();
        }

        var lookup = await sessions.FindAsync(sessionId, renew: false, Context.RequestAborted);
        switch (lookup.State)
        {
            case WebSessionState.Missing:
            case WebSessionState.Refused:
                WebSessionCookies.ClearSession(Response, settings);
                return AuthenticateResult.Fail("The web session has ended.");
            case WebSessionState.Unavailable:
                _failure = Failure.Unavailable;
                return AuthenticateResult.Fail("The web session could not be renewed because Keycloak cannot be reached.");
        }

        var session = lookup.Session!;
        if (!IsSafeMethod(Request.Method) && !PassesCsrfCheck(session.CsrfToken))
        {
            _failure = Failure.Csrf;
            return AuthenticateResult.Fail("The request failed the CSRF check of the web session.");
        }

        var principal = await validator.ValidateAsync(lookup.Tokens!.AccessToken, Context.RequestAborted);
        return principal is null
            ? AuthenticateResult.Fail("The access token of the web session is not valid.")
            : AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        switch (_failure)
        {
            case Failure.Csrf:
                await WriteProblemAsync(
                    StatusCodes.Status403Forbidden,
                    "Request refused",
                    "The request did not carry the CSRF token of the web session or came from another origin.",
                    CsrfRejectedCode);
                break;
            case Failure.Unavailable:
                await WriteProblemAsync(
                    StatusCodes.Status503ServiceUnavailable,
                    "Sign-in service unavailable",
                    "The session could not be renewed because the sign-in service cannot be reached. The session stays; try again later.",
                    IdentityProviderUnavailableCode);
                break;
            default:
                await base.HandleChallengeAsync(properties);
                break;
        }
    }

    private bool PassesCsrfCheck(string expectedToken)
    {
        var origin = Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !settings.AllowedOrigins.Contains(origin, StringComparer.Ordinal))
        {
            return false;
        }

        var sent = Request.Headers[CsrfHeader].ToString();
        return sent.Length > 0
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(expectedToken));
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);

    private async Task WriteProblemAsync(int status, string title, string detail, string code)
    {
        Response.StatusCode = status;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        });
    }
}
