using System.Security.Claims;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// What a viewer may see of one tank: the levels of the tank and how the viewer relates to the tank (owner,
/// selected member, approved follower or other member).
/// </summary>
public sealed record TankAccess(long TankId, long OwnerProfileId, TankVisibility Profile, TankVisibility Stream, TankViewer Viewer)
{
    public bool IsOwner => Viewer == TankViewer.Owner;

    public bool CanSeeProfile => TankVisibilityRules.CanSeeProfile(Viewer, Profile);

    public bool CanSeeStream => TankVisibilityRules.CanSeeStream(Viewer, Profile, Stream);
}

/// <summary>
/// Resolves who a caller is for the tank visibility rules and what that caller may see of a tank.
/// A caller without a profile of their own, whether without an account or with an account that has
/// no profile yet, is anonymous.
/// </summary>
public sealed class TankAccessService(CichlidsDbContext context)
{
    /// <summary>
    /// The profile id of the authenticated caller, or null when the caller is anonymous. A suspended profile
    /// is anonymous, so a follow or a selection never keeps a suspended member in an audience.
    /// </summary>
    public async Task<long?> ViewerProfileIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var subject = user.FindFirst("sub")?.Value;
        if (user.Identity?.IsAuthenticated != true || subject is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        return await context.ProfileIdentities.AsNoTracking()
            .Where(i => i.Provider == "oidc" && i.Subject == subject)
            .Join(context.Profiles, i => i.ProfileId, p => p.Id, (i, p) => p)
            .Where(p => p.SuspendedUntil == null || p.SuspendedUntil <= now)
            .Select(p => (long?)p.Id).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The access of the caller to a tank that is not deleted, or null when there is no such tank.
    /// Whether the caller may see the tank or its stream is the answer of the returned access, so
    /// callers answer a tank they may not see exactly as a missing tank.
    /// </summary>
    public async Task<TankAccess?> FindAsync(long tankId, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var tank = await context.Tanks.AsNoTracking().Where(t => t.Id == tankId && t.DeletedAt == null)
            .Select(t => new { t.ProfileId, t.ProfileVisibility, t.StreamVisibility }).SingleOrDefaultAsync(cancellationToken);
        if (tank is null)
        {
            return null;
        }

        var (isFollower, isSelected) = await context.RelationToTankAsync(tankId, viewerProfileId, cancellationToken);
        return new TankAccess(
            tankId, tank.ProfileId, tank.ProfileVisibility, tank.StreamVisibility,
            TankVisibilityRules.ViewerOf(viewerProfileId, tank.ProfileId, isFollower, isSelected));
    }

    /// <summary>
    /// Finds the access of the caller to a tank, resolving the caller first.
    /// </summary>
    public async Task<TankAccess?> FindAsync(long tankId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await FindAsync(tankId, await ViewerProfileIdAsync(user, cancellationToken), cancellationToken);
}
