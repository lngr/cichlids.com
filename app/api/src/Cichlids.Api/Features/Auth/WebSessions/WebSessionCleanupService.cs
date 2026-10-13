using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>
/// One cleanup pass over web sessions that nobody can use any more: a session whose last use is
/// older than the retention is deleted after its offline session at Keycloak is revoked. A
/// session whose revocation got no answer stays for the next pass, so no offline session is left
/// without a row that knows its token. A session whose refresh token the key ring cannot decrypt
/// is deleted without a revocation. Several instances may run a pass at the same time; the
/// delete of a session happens once, and a second revocation of the same token is harmless.
/// </summary>
public sealed class WebSessionCleanupService(
    CichlidsDbContext context,
    KeycloakOidcClient keycloak,
    WebSessionTokenProtector protector,
    IOptions<WebSessionCleanupOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Deletes up to one batch of unused sessions and returns how many this pass deleted.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = timeProvider.GetUtcNow() - settings.Retention;

        var stale = await context.WebSessions
            .Where(s => s.LastSeenAt < cutoff)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.EncryptedRefreshToken })
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        var deleted = 0;
        foreach (var session in stale)
        {
            // A refresh token that the key ring cannot decrypt cannot be revoked either.
            if (protector.TryDecrypt(session.EncryptedRefreshToken, out var refreshToken)
                && refreshToken.Length > 0
                && !await keycloak.EndSessionAsync(refreshToken, cancellationToken))
            {
                continue;
            }

            // The condition in the delete itself skips a session that was used or deleted by
            // another request or instance since it was selected.
            deleted += await context.WebSessions
                .Where(s => s.Id == session.Id && s.LastSeenAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return deleted;
    }
}
