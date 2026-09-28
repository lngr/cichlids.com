using System.Security.Claims;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Resolves the authenticated principal to its profile through the token's subject, the account
/// id, and an oidc identity binding it. A migrated member reaches the migrated profile through the
/// oidc identity the legacy account import creates; the login method and the token's email play
/// no part. Without such an identity the call creates a fresh member profile with an oidc identity,
/// so the first authenticated call is all a new account needs to exist in the domain. A fresh
/// profile never shows an email address: its handle and display name come from the token only
/// when they hold none.
/// </summary>
public sealed class CurrentProfileService(CichlidsDbContext context, Lazy<GeneratedNames> generatedNames)
{
    private const string OidcProvider = "oidc";

    public async Task<Profile> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var subject = user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("The authenticated principal has no sub claim.");

        var bySubject = await context.ProfileIdentities
            .Where(i => i.Provider == OidcProvider && i.Subject == subject)
            .Join(context.Profiles, i => i.ProfileId, p => p.Id, (_, p) => p)
            .FirstOrDefaultAsync(cancellationToken);

        return bySubject ?? await CreateProfileAsync(user, subject, cancellationToken);
    }

    private async Task<Profile> CreateProfileAsync(
        ClaimsPrincipal user, string subject, CancellationToken cancellationToken)
    {
        var username = await ResolveFreeUsernameAsync(
            user.FindFirst("preferred_username")?.Value, subject, cancellationToken);
        var name = user.FindFirst("name")?.Value;

        var profile = new Profile
        {
            Username = username,
            DisplayName = PublicHandle.IsUsable(name) ? name! : username,
            Kind = ProfileKind.Member,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.Profiles.Add(profile);
        await context.SaveChangesAsync(cancellationToken);

        context.ProfileIdentities.Add(new ProfileIdentity
        {
            ProfileId = profile.Id,
            Provider = OidcProvider,
            Subject = subject,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync(cancellationToken);

        return profile;
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
