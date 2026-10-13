using System.Security.Cryptography;
using System.Text;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Auth.WebSessions;

public enum WebSessionState
{
    /// <summary>No session has this id, or its tokens cannot be decrypted, in which case it is deleted.</summary>
    Missing,

    /// <summary>The session is usable; its access token is valid.</summary>
    Active,

    /// <summary>Keycloak refused to renew the session, which is deleted.</summary>
    Refused,

    /// <summary>The access token needs renewal and Keycloak gave no usable answer; the session stays.</summary>
    Unavailable,
}

/// <summary>The decrypted Keycloak tokens of a web session; IdToken is null when Keycloak issued none.</summary>
public sealed record WebSessionTokens(string AccessToken, string RefreshToken, string? IdToken);

/// <summary>The state of a looked-up session; Session and Tokens are null for Missing and Refused.</summary>
public sealed record WebSessionLookup(WebSessionState State, WebSession? Session, WebSessionTokens? Tokens);

/// <summary>
/// Creates, finds, renews and ends the server-side web sessions. A session is found through the
/// hash of its id, and its tokens are stored encrypted. Its access token is renewed with the
/// refresh token shortly before it expires: a refusal of Keycloak deletes the session, an
/// unreachable Keycloak leaves it in place. A session whose tokens the key ring cannot decrypt is
/// deleted and counts as missing. Renewals of one session in this process run one at a time.
/// </summary>
public sealed class WebSessionService(
    CichlidsDbContext db,
    KeycloakOidcClient keycloak,
    WebSessionTokenProtector protector,
    WebSessionLocks locks,
    TimeProvider timeProvider,
    ILogger<WebSessionService> logger)
{
    /// <summary>An access token this close to its expiry is renewed before use.</summary>
    public static readonly TimeSpan RenewalMargin = TimeSpan.FromSeconds(30);

    /// <summary>Stores a session for the tokens of a login and returns the new session id.</summary>
    public async Task<string> CreateAsync(IssuedTokens tokens, string subject, CancellationToken cancellationToken)
    {
        var sessionId = RandomToken();
        var now = timeProvider.GetUtcNow();
        var session = new WebSession
        {
            SessionIdHash = Hash(sessionId),
            Subject = subject,
            AccessTokenExpiresAt = now.AddSeconds(tokens.ExpiresIn),
            CsrfToken = RandomToken(),
            CreatedAt = now,
            LastSeenAt = now,
        };
        Store(session, new WebSessionTokens(tokens.AccessToken, tokens.RefreshToken ?? string.Empty, tokens.IdToken));
        db.WebSessions.Add(session);
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
            return new WebSessionLookup(WebSessionState.Missing, null, null);
        }

        if (!renew && !NeedsRenewal(session))
        {
            return await ActiveAsync(session, cancellationToken);
        }

        using (await locks.AcquireAsync(session.Id, cancellationToken))
        {
            // Another request may have renewed or ended the session while this one waited.
            await db.Entry(session).ReloadAsync(cancellationToken);
            if (db.Entry(session).State == EntityState.Detached)
            {
                return new WebSessionLookup(WebSessionState.Missing, null, null);
            }

            if (!renew && !NeedsRenewal(session))
            {
                return await ActiveAsync(session, cancellationToken);
            }

            var tokens = Decrypt(session);
            if (tokens is null)
            {
                return await DiscardAsync(session, cancellationToken);
            }

            var result = await keycloak.RefreshAsync(tokens.RefreshToken, cancellationToken);
            switch (result.Outcome)
            {
                case TokenRequestOutcome.Issued:
                    var issued = result.Tokens!;
                    var renewed = new WebSessionTokens(
                        issued.AccessToken,
                        issued.RefreshToken ?? tokens.RefreshToken,
                        issued.IdToken ?? tokens.IdToken);
                    Store(session, renewed);
                    session.AccessTokenExpiresAt = timeProvider.GetUtcNow().AddSeconds(issued.ExpiresIn);
                    await db.SaveChangesAsync(cancellationToken);
                    return new WebSessionLookup(WebSessionState.Active, session, renewed);
                case TokenRequestOutcome.Refused:
                    db.WebSessions.Remove(session);
                    await db.SaveChangesAsync(cancellationToken);
                    return new WebSessionLookup(WebSessionState.Refused, null, null);
                default:
                    return new WebSessionLookup(
                        NeedsRenewal(session) ? WebSessionState.Unavailable : WebSessionState.Active, session, tokens);
            }
        }
    }

    /// <summary>Records that the web app opened with the session.</summary>
    public async Task TouchAsync(WebSession session, CancellationToken cancellationToken)
    {
        session.LastSeenAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes the session and returns its tokens, or null when no session has this id or its
    /// tokens cannot be decrypted.
    /// </summary>
    public async Task<WebSessionTokens?> EndAsync(string sessionId, CancellationToken cancellationToken)
    {
        var hash = Hash(sessionId);
        var session = await db.WebSessions.SingleOrDefaultAsync(s => s.SessionIdHash == hash, cancellationToken);
        if (session is null)
        {
            return null;
        }

        db.WebSessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);
        return Decrypt(session);
    }

    /// <summary>A random URL-safe token of 256 bits.</summary>
    public static string RandomToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private async Task<WebSessionLookup> ActiveAsync(WebSession session, CancellationToken cancellationToken)
    {
        var tokens = Decrypt(session);
        return tokens is null
            ? await DiscardAsync(session, cancellationToken)
            : new WebSessionLookup(WebSessionState.Active, session, tokens);
    }

    // Nobody can use a session whose tokens the key ring cannot decrypt, and its offline session
    // at Keycloak cannot be revoked without the refresh token.
    private async Task<WebSessionLookup> DiscardAsync(WebSession session, CancellationToken cancellationToken)
    {
        logger.LogWarning("The tokens of web session {SessionId} cannot be decrypted with the key ring; the session is deleted.", session.Id);
        db.WebSessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);
        return new WebSessionLookup(WebSessionState.Missing, null, null);
    }

    private WebSessionTokens? Decrypt(WebSession session)
    {
        if (!protector.TryDecrypt(session.EncryptedAccessToken, out var accessToken)
            || !protector.TryDecrypt(session.EncryptedRefreshToken, out var refreshToken))
        {
            return null;
        }

        string? idToken = null;
        if (session.EncryptedIdToken is not null && !protector.TryDecrypt(session.EncryptedIdToken, out idToken))
        {
            return null;
        }

        return new WebSessionTokens(accessToken, refreshToken, idToken);
    }

    private void Store(WebSession session, WebSessionTokens tokens)
    {
        session.EncryptedAccessToken = protector.Encrypt(tokens.AccessToken);
        session.EncryptedRefreshToken = protector.Encrypt(tokens.RefreshToken);
        session.EncryptedIdToken = tokens.IdToken is null ? null : protector.Encrypt(tokens.IdToken);
    }

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
