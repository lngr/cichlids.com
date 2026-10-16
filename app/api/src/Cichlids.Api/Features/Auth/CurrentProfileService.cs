using System.Security.Claims;
using Cichlids.Api.Features.Auth.Devices;
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
/// candidate. A provisional caller (device token) has no oidc identity: ResolveAsync refuses it
/// and ResolveCallerAsync returns its provisional profile. A subject bound to a profile of another kind than member is refused with a
/// NonMemberLoginException. A fresh profile never shows an email address or the name of a system
/// profile: the token's username and name become its handle and display name only when they are
/// neither email-like nor reserved.
/// </summary>
public sealed class CurrentProfileService(
    CichlidsDbContext context,
    Lazy<GeneratedNames> generatedNames,
    ILogger<CurrentProfileService> logger)
{
    private const string OidcProvider = "oidc";
    private const int MaxCreateAttempts = 3;

    /// <summary>
    /// The profile of a registered member. A provisional caller raises a RegistrationRequiredException.
    /// </summary>
    public async Task<Profile> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.IsProvisional())
        {
            throw new RegistrationRequiredException();
        }

        var subject = user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("The authenticated principal has no sub claim.");

        for (var attempt = 1; attempt <= MaxCreateAttempts; attempt++)
        {
            var existing = await FindBySubjectAsync(subject, cancellationToken);
            if (existing is not null)
            {
                return RequireMember(existing, subject);
            }

            var created = await TryCreateProfileAsync(user, subject, cancellationToken);
            if (created is not null)
            {
                return created;
            }
        }

        var winner = await FindBySubjectAsync(subject, cancellationToken);
        if (winner is not null)
        {
            return RequireMember(winner, subject);
        }

        throw new InvalidOperationException(
            $"The profile for a first login could not be created after {MaxCreateAttempts} attempts.");
    }

    /// <summary>
    /// The profile of the caller, a registered member or the provisional profile of a device token.
    /// </summary>
    public async Task<Profile> ResolveCallerAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.ProvisionalProfileId() is not { } profileId)
        {
            return await ResolveAsync(user, cancellationToken);
        }

        return await context.Profiles.FirstOrDefaultAsync(p => p.Id == profileId && p.Kind == ProfileKind.Provisional, cancellationToken)
            ?? throw new InvalidOperationException($"The device token names profile {profileId}, which is not a provisional profile.");
    }

    /// <summary>
    /// The member profile of the Keycloak account behind the principal, or null when the account has
    /// no profile yet. Nothing is created. A provisional caller raises a RegistrationRequiredException
    /// and an account bound to a profile of another kind than member a NonMemberLoginException.
    /// </summary>
    public async Task<Profile?> FindMemberAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.IsProvisional())
        {
            throw new RegistrationRequiredException();
        }

        var subject = user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("The authenticated principal has no sub claim.");
        var existing = await FindBySubjectAsync(subject, cancellationToken);
        return existing is null ? null : RequireMember(existing, subject);
    }

    private Profile RequireMember(Profile profile, string subject)
    {
        if (profile.Kind == ProfileKind.Member)
        {
            return profile;
        }

        logger.LogWarning(
            "Login refused: subject {Subject} is bound to profile {ProfileId} of kind {ProfileKind}.",
            subject, profile.Id, profile.Kind);
        throw new NonMemberLoginException(profile.Id, profile.Kind);
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
        var (username, displayName) = await MemberNames.ResolveAsync(
            context, generatedNames.Value, user, subject, cancellationToken);
        var createdAt = DateTimeOffset.UtcNow;

        var identity = new ProfileIdentity { Provider = OidcProvider, Subject = subject, CreatedAt = createdAt };
        var profile = new Profile
        {
            Username = username,
            DisplayName = displayName,
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
}
