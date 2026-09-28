using System.Security.Claims;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Resolves the authenticated principal to its profile. Resolution order: the token's subject
/// against an existing oidc identity; then, only when the token's email_verified claim is true,
/// the token's email against an email identity from the legacy user migration, which links the
/// migrated profile to the new login by creating its oidc identity; and as a last resort a fresh
/// member profile with an oidc identity, so the first authenticated call is all a new user needs
/// to exist in the domain. An unverified email never reaches a migrated profile, because anyone
/// can enter any address at registration.
/// </summary>
public sealed class CurrentProfileService(CichlidsDbContext context)
{
    private const string OidcProvider = "oidc";
    private const string EmailProvider = "email";

    public async Task<Profile> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var subject = user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("The authenticated principal has no sub claim.");

        var bySubject = await FindByIdentityAsync(OidcProvider, subject, cancellationToken);
        if (bySubject is not null)
        {
            return bySubject;
        }

        var email = user.FindFirst("email")?.Value?.Trim().ToLowerInvariant();
        var emailVerified = bool.TryParse(user.FindFirst("email_verified")?.Value, out var verified) && verified;
        if (emailVerified && !string.IsNullOrEmpty(email))
        {
            var byEmail = await FindByIdentityAsync(EmailProvider, email, cancellationToken);
            if (byEmail is not null)
            {
                context.ProfileIdentities.Add(new ProfileIdentity
                {
                    ProfileId = byEmail.Id,
                    Provider = OidcProvider,
                    Subject = subject,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await context.SaveChangesAsync(cancellationToken);
                return byEmail;
            }
        }

        return await CreateProfileAsync(user, subject, cancellationToken);
    }

    private Task<Profile?> FindByIdentityAsync(string provider, string subject, CancellationToken cancellationToken) =>
        context.ProfileIdentities
            .Where(i => i.Provider == provider && i.Subject == subject)
            .Join(context.Profiles, i => i.ProfileId, p => p.Id, (_, p) => p)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Profile> CreateProfileAsync(
        ClaimsPrincipal user, string subject, CancellationToken cancellationToken)
    {
        var username = await ResolveFreeUsernameAsync(
            user.FindFirst("preferred_username")?.Value, subject, cancellationToken);

        var profile = new Profile
        {
            Username = username,
            DisplayName = user.FindFirst("name")?.Value ?? username,
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
    /// Picks the first free username: the token's preferred username as-is, then with a numeric
    /// suffix counting up from 2. The subject stands in when the token has no usable username.
    /// </summary>
    private async Task<string> ResolveFreeUsernameAsync(
        string? preferredUsername, string subject, CancellationToken cancellationToken)
    {
        var baseUsername = string.IsNullOrWhiteSpace(preferredUsername) ? subject : preferredUsername.Trim();

        var candidate = baseUsername;
        for (var suffix = 2; await context.Profiles.AnyAsync(p => p.Username == candidate, cancellationToken); suffix++)
        {
            candidate = $"{baseUsername}-{suffix}";
        }

        return candidate;
    }
}
