using System.Security.Claims;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Resolves the authenticated principal to its profile through the token's subject, the account
/// id, and an oidc identity binding it. A migrated member reaches the migrated profile through the
/// oidc identity the legacy account import creates; the login method and the token's email play
/// no part. Without such an identity the call creates a fresh member profile together with its
/// oidc identity in a single save, so a profile never exists without its identity. When a
/// parallel first call wins the race for the same subject, the call returns the profile that won;
/// when another account takes the chosen username first, the call retries with a fresh
/// candidate. A fresh profile never shows an email address: its handle and display name come from
/// the token only when they hold none.
/// </summary>
public sealed class CurrentProfileService(CichlidsDbContext context, Lazy<GeneratedNames> generatedNames)
{
    private const string OidcProvider = "oidc";
    private const int MaxCreateAttempts = 3;

    public async Task<Profile> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var subject = user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("The authenticated principal has no sub claim.");

        for (var attempt = 1; attempt <= MaxCreateAttempts; attempt++)
        {
            var existing = await FindBySubjectAsync(subject, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var created = await TryCreateProfileAsync(user, subject, cancellationToken);
            if (created is not null)
            {
                return created;
            }
        }

        throw new InvalidOperationException(
            $"The profile for a first login could not be created after {MaxCreateAttempts} attempts.");
    }

    private Task<Profile?> FindBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        context.ProfileIdentities
            .Where(i => i.Provider == OidcProvider && i.Subject == subject)
            .Join(context.Profiles, i => i.ProfileId, p => p.Id, (_, p) => p)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Adds the profile with its oidc identity and saves once. A unique violation means a
    /// parallel call took the subject or the username; the two new entries are detached, since
    /// the context is shared with the rest of the request, and null tells the caller to look
    /// again.
    /// </summary>
    private async Task<Profile?> TryCreateProfileAsync(
        ClaimsPrincipal user, string subject, CancellationToken cancellationToken)
    {
        var username = await ResolveFreeUsernameAsync(
            user.FindFirst("preferred_username")?.Value, subject, cancellationToken);
        var name = user.FindFirst("name")?.Value;
        var createdAt = DateTimeOffset.UtcNow;

        var identity = new ProfileIdentity { Provider = OidcProvider, Subject = subject, CreatedAt = createdAt };
        var profile = new Profile
        {
            Username = username,
            DisplayName = PublicHandle.IsUsable(name) ? name! : username,
            Kind = ProfileKind.Member,
            CreatedAt = createdAt,
            Identities = [identity],
        };
        context.Profiles.Add(profile);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return profile;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.Entry(identity).State = EntityState.Detached;
            context.Entry(profile).State = EntityState.Detached;
            return null;
        }
    }

    /// <summary>
    /// Picks the first free handle. A usable preferred username is taken as-is, then with a
    /// numeric suffix counting up from 2. A blank or email-like preferred username yields the
    /// generated handles keyed by the subject instead.
    /// </summary>
    private Task<string> ResolveFreeUsernameAsync(
        string? preferredUsername, string subject, CancellationToken cancellationToken) =>
        FirstFreeAsync(
            PublicHandle.IsUsable(preferredUsername)
                ? SuffixedCandidates(preferredUsername!.Trim())
                : generatedNames.Value.Candidates(GeneratedNames.HandleInput(subject)),
            cancellationToken);

    private static IEnumerable<string> SuffixedCandidates(string baseUsername)
    {
        yield return baseUsername;
        for (var suffix = 2; ; suffix++)
        {
            yield return $"{baseUsername}-{suffix}";
        }
    }

    private async Task<string> FirstFreeAsync(IEnumerable<string> candidates, CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            if (!await context.Profiles.AnyAsync(p => p.Username == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The candidate sequence ended without a free username.");
    }
}
