using System.Security.Claims;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Chooses the public handle and the display name of a profile that becomes a member of a Keycloak
/// account. A usable preferred username of the token is taken as it is, then with a numeric suffix
/// counting up from 2 until a free handle is found. A blank, email-like or reserved preferred
/// username yields the generated handles keyed by the account id. The display name is the name of
/// the token when it is usable and not reserved, otherwise the handle.
/// </summary>
internal static class MemberNames
{
    public static async Task<(string Username, string DisplayName)> ResolveAsync(
        CichlidsDbContext context, GeneratedNames generatedNames, ClaimsPrincipal user, string subject,
        CancellationToken cancellationToken)
    {
        var preferredUsername = user.FindFirst("preferred_username")?.Value;
        var username = await FirstFreeAsync(
            context,
            PublicHandle.IsUsable(preferredUsername) && !ReservedUsernames.IsReserved(preferredUsername)
                ? SuffixedCandidates(preferredUsername!.Trim())
                : generatedNames.Candidates(GeneratedNames.HandleInput(subject)),
            cancellationToken);
        var name = user.FindFirst("name")?.Value;
        return (username, PublicHandle.IsUsable(name) && !ReservedUsernames.IsReserved(name) ? name! : username);
    }

    private static IEnumerable<string> SuffixedCandidates(string baseUsername)
    {
        yield return baseUsername;
        for (var suffix = 2; ; suffix++)
        {
            yield return $"{baseUsername}-{suffix}";
        }
    }

    private static async Task<string> FirstFreeAsync(
        CichlidsDbContext context, IEnumerable<string> candidates, CancellationToken cancellationToken)
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
