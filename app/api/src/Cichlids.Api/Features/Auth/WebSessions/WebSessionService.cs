using System.Security.Cryptography;
using System.Text;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Auth.WebSessions;

public enum WebSessionState
{
    /// <summary>No session has this id.</summary>
    Missing,

    /// <summary>The session is usable; its access token is valid.</summary>
    Active,

    /// <summary>Keycloak refused to renew the session, which is deleted.</summary>
    Refused,

    /// <summary>The access token needs renewal and Keycloak gave no usable answer; the session stays.</summary>
    Unavailable,
}

/// <summary>The state of a looked-up session; Session is null for Missing and Refused.</summary>
public sealed record WebSessionLookup(WebSessionState State, WebSession? Session);

/// <summary>
/// Creates, finds, renews and ends the server-side web sessions. A session is found through the
/// hash of its id. Its access token is renewed with the refresh token shortly before it expires:
/// a refusal of Keycloak deletes the session, an unreachable Keycloak leaves it in place.
/// Renewals of one session in this process run one at a time.
/// </summary>
public sealed class WebSessionService(
    CichlidsDbContext db,
    KeycloakOidcClient keycloak,
    WebSessionLocks locks,
    TimeProvider timeProvider)
{
    /// <summary>An access token this close to its expiry is renewed before use.</summary>
    public static readonly TimeSpan RenewalMargin = TimeSpan.FromSeconds(30);

    /// <summary>Stores a session for the tokens of a login and returns the new session id.</summary>
    public async Task<string> CreateAsync(IssuedTokens tokens, string subject, CancellationToken cancellationToken)
    {
        var sessionId = RandomToken();
        var now = timeProvider.GetUtcNow();
        db.WebSessions.Add(new WebSession
        {
            SessionIdHash = Hash(sessionId),
            Subject = subject,
            AccessToken = tokens.AccessToken,
            AccessTokenExpiresAt = now.AddSeconds(tokens.ExpiresIn),
            RefreshToken = tokens.RefreshToken ?? string.Empty,
            IdToken = tokens.IdToken,
            CsrfToken = RandomToken(),
            CreatedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        return sessionId;
    }

    /// <summary>
    /// Finds the session and renews its access token when it is about to expire, or always when
    /// renew is set. With renew set, an unreachable Keycloak still yields Active while the access
    /// token is valid.
    /// </summary>
    public async Task<WebSessionLookup> FindAsync(string sessionId, bool renew, CancellationToken cancellationToken)
    {
        var hash = Hash(sessionId);
        var session = await db.WebSessions.SingleOrDefaultAsync(s => s.SessionIdHash == hash, cancellationToken);
        if (session is null)
        {
            return new WebSessionLookup(WebSessionState.Missing, null);
        }

        if (!renew && !NeedsRenewal(session))
        {
            return new WebSessionLookup(WebSessionState.Active, session);
        }

        using (await locks.AcquireAsync(session.Id, cancellationToken))
        {
            // Another request may have renewed or ended the session while this one waited.
            await db.Entry(session).ReloadAsync(cancellationToken);
            if (db.Entry(session).State == EntityState.Detached)
            {
                return new WebSessionLookup(WebSessionState.Missing, null);
            }

            if (!renew && !NeedsRenewal(session))
            {
                return new WebSessionLookup(WebSessionState.Active, session);
            }

            var result = await keycloak.RefreshAsync(session.RefreshToken, cancellationToken);
            switch (result.Outcome)
            {
                case TokenRequestOutcome.Issued:
                    var tokens = result.Tokens!;
                    session.AccessToken = tokens.AccessToken;
                    session.AccessTokenExpiresAt = timeProvider.GetUtcNow().AddSeconds(tokens.ExpiresIn);
                    session.RefreshToken = tokens.RefreshToken ?? session.RefreshToken;
                    session.IdToken = tokens.IdToken ?? session.IdToken;
                    await db.SaveChangesAsync(cancellationToken);
                    return new WebSessionLookup(WebSessionState.Active, session);
                case TokenRequestOutcome.Refused:
                    db.WebSessions.Remove(session);
                    await db.SaveChangesAsync(cancellationToken);
                    return new WebSessionLookup(WebSessionState.Refused, null);
                default:
                    return new WebSessionLookup(NeedsRenewal(session) ? WebSessionState.Unavailable : WebSessionState.Active, session);
            }
        }
    }

    /// <summary>Records that the web app opened with the session.</summary>
    public async Task TouchAsync(WebSession session, CancellationToken cancellationToken)
    {
        session.LastSeenAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Deletes the session and returns it, or null when no session has this id.</summary>
    public async Task<WebSession?> EndAsync(string sessionId, CancellationToken cancellationToken)
    {
        var hash = Hash(sessionId);
        var session = await db.WebSessions.SingleOrDefaultAsync(s => s.SessionIdHash == hash, cancellationToken);
        if (session is null)
        {
            return null;
        }

        db.WebSessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    /// <summary>A random URL-safe token of 256 bits.</summary>
    public static string RandomToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private bool NeedsRenewal(WebSession session) =>
        timeProvider.GetUtcNow() >= session.AccessTokenExpiresAt - RenewalMargin;

    private static byte[] Hash(string sessionId) => SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
}

/// <summary>
/// Serializes the renewals of one session within this process, so parallel requests of a web app
/// renew its tokens once. The sessions share a fixed set of locks by id.
/// </summary>
public sealed class WebSessionLocks
{
    private readonly SemaphoreSlim[] _stripes = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<IDisposable> AcquireAsync(long sessionId, CancellationToken cancellationToken)
    {
        var stripe = _stripes[(int)(sessionId % _stripes.Length)];
        await stripe.WaitAsync(cancellationToken);
        return new Release(stripe);
    }

    private sealed class Release(SemaphoreSlim stripe) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                stripe.Release();
            }
        }
    }
}
