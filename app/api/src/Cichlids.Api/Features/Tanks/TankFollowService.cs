using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// The follow relations of tanks. A member follows a public tank at once and sends a follow request to a
/// tank whose profile level is followers; the owner approves or declines requests and removes followers,
/// and the member withdraws a request or stops following. Every mutation locks the tank row, so it
/// serializes with a change of the tank levels, and commits with its outbox event. The error of a result
/// is a problem code. A follow of a public tank stays an approved follow when the owner narrows the tank to
/// followers: the owner keeps or removes such followers one by one, and a member whose follow was removed
/// or declined needs a new request that the owner approves.
/// </summary>
public sealed class TankFollowService(CichlidsDbContext context, IObjectStore objectStore)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public const string NotFoundCode = "tank_not_found";
    public const string FollowNotAllowedCode = "tank_follow_not_allowed";
    public const string RequestNotFoundCode = "follow_request_not_found";
    public const string FollowerNotFoundCode = "follower_not_found";

    /// <summary>
    /// The follow relation of the caller to a tank they may know of, or null when the tank does not exist
    /// or neither its profile nor a follow request is open to the caller. The teaser carries the owner only. Callers answer null as a
    /// missing tank.
    /// </summary>
    public async Task<TankFollowDto?> GetAsync(long tankId, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var tank = await context.Tanks.AsNoTracking().Where(t => t.Id == tankId && t.DeletedAt == null && t.State == TankState.Published)
            .Select(t => new { t.ProfileId, t.ProfileVisibility }).SingleOrDefaultAsync(cancellationToken);
        if (tank is null)
        {
            return null;
        }

        var (isFollower, isSelected) = await context.RelationToTankAsync(tankId, viewerProfileId, cancellationToken);
        var viewer = TankVisibilityRules.ViewerOf(viewerProfileId, tank.ProfileId, isFollower, isSelected);
        var canSeeProfile = TankVisibilityRules.CanSeeProfile(viewer, tank.ProfileVisibility);
        var takesRequests = viewerProfileId is not null && TankVisibilityRules.CanFollow(viewer, tank.ProfileVisibility);
        if (!canSeeProfile && !takesRequests)
        {
            return null;
        }

        var status = viewerProfileId is { } id
            ? await context.Follows.AsNoTracking().Where(f => f.TargetTankId == tankId && f.FollowerProfileId == id)
                .Select(f => (FollowStatus?)f.Status).SingleOrDefaultAsync(cancellationToken)
            : null;
        var state = status switch
        {
            FollowStatus.Approved => TankFollowState.Following,
            FollowStatus.Pending => TankFollowState.Requested,
            _ => TankFollowState.None,
        };
        TankFollowTeaserDto? teaser = null;
        if (!canSeeProfile)
        {
            var owner = (await AuthorBatchLoader.LoadAsync(context, [tank.ProfileId], cancellationToken))[tank.ProfileId];
            teaser = new TankFollowTeaserDto(owner.ToDto(objectStore));
        }

        return new TankFollowDto(state, takesRequests && state == TankFollowState.None, teaser);
    }

    /// <summary>
    /// Follows a tank for the member: at once when its profile is public, as a pending request when its
    /// profile level is followers. Repeating the call keeps the existing relation.
    /// </summary>
    public async Task<(TankFollowState? State, string? Error)> FollowAsync(long tankId, long profileId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tank = await LockTankAsync(tankId, cancellationToken);
        if (tank is null || tank.State != TankState.Published)
        {
            return (null, NotFoundCode);
        }

        var now = DateTimeOffset.UtcNow;
        if (await context.Profiles.AnyAsync(p => p.Id == profileId && p.SuspendedUntil != null && p.SuspendedUntil > now, cancellationToken))
        {
            return (null, FollowNotAllowedCode);
        }

        var existing = await context.Follows.SingleOrDefaultAsync(f => f.TargetTankId == tankId && f.FollowerProfileId == profileId, cancellationToken);
        var (isFollower, isSelected) = await context.RelationToTankAsync(tankId, profileId, cancellationToken);
        var viewer = TankVisibilityRules.ViewerOf(profileId, tank.ProfileId, isFollower, isSelected);
        if (viewer == TankViewer.Owner)
        {
            return (null, FollowNotAllowedCode);
        }

        if (!TankVisibilityRules.CanFollow(viewer, tank.ProfileVisibility))
        {
            return TankVisibilityRules.CanSeeProfile(viewer, tank.ProfileVisibility) ? (null, FollowNotAllowedCode) : (null, NotFoundCode);
        }

        if (existing is not null)
        {
            return (existing.Status == FollowStatus.Approved ? TankFollowState.Following : TankFollowState.Requested, null);
        }

        var immediate = !TankVisibilityRules.FollowNeedsApproval(tank.ProfileVisibility);
        context.Follows.Add(new Follow
        {
            FollowerProfileId = profileId, TargetTankId = tankId, CreatedAt = now,
            Status = immediate ? FollowStatus.Approved : FollowStatus.Pending, DecidedAt = immediate ? now : null,
        });
        AddEvent(immediate ? "tank.followed" : "tank.follow_requested", tank, profileId, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (immediate ? TankFollowState.Following : TankFollowState.Requested, null);
    }

    /// <summary>
    /// Stops following a tank or withdraws the open request of the member. Without a relation there is
    /// nothing to remove.
    /// </summary>
    public async Task<string?> UnfollowAsync(long tankId, long profileId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tank = await LockTankAsync(tankId, cancellationToken);
        if (tank is null)
        {
            return NotFoundCode;
        }

        var follow = await context.Follows.SingleOrDefaultAsync(f => f.TargetTankId == tankId && f.FollowerProfileId == profileId, cancellationToken);
        if (follow is null)
        {
            // Without a relation the answer for a tank the caller has no access to is the answer for a missing tank.
            var (isFollower, isSelected) = await context.RelationToTankAsync(tankId, profileId, cancellationToken);
            var viewer = TankVisibilityRules.ViewerOf(profileId, tank.ProfileId, isFollower, isSelected);
            var known = tank.State == TankState.Published
                && (TankVisibilityRules.CanSeeProfile(viewer, tank.ProfileVisibility) || TankVisibilityRules.CanFollow(viewer, tank.ProfileVisibility));
            return known ? null : NotFoundCode;
        }

        context.Follows.Remove(follow);
        AddEvent(follow.Status == FollowStatus.Approved ? "tank.unfollowed" : "tank.follow_withdrawn", tank, profileId, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    /// <summary>
    /// The approved followers (status approved) or the open requests (status pending) of the owner's tank,
    /// oldest first, or null when the tank is not the caller's.
    /// </summary>
    public async Task<PagedResponse<TankMemberDto>?> ListAsync(
        long tankId, long ownerProfileId, FollowStatus status, int offset, int limit, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(tankId, ownerProfileId, cancellationToken))
        {
            return null;
        }

        var follows = context.Follows.AsNoTracking().Where(f => f.TargetTankId == tankId && f.Status == status);
        var total = await follows.CountAsync(cancellationToken);
        var rows = await follows.OrderBy(f => f.DecidedAt ?? f.CreatedAt).ThenBy(f => f.Id).Skip(offset).Take(limit)
            .Select(f => new { f.FollowerProfileId, Since = f.DecidedAt ?? f.CreatedAt }).ToListAsync(cancellationToken);
        var authors = await AuthorBatchLoader.LoadAsync(context, rows.Select(r => r.FollowerProfileId).ToList(), cancellationToken);
        return new PagedResponse<TankMemberDto>(total, rows.Select(r => new TankMemberDto(authors[r.FollowerProfileId].ToDto(objectStore), r.Since)).ToList());
    }

    /// <summary>
    /// Approves an open follow request of the owner's tank.
    /// </summary>
    public Task<string?> ApproveAsync(long tankId, long ownerProfileId, long followerProfileId, CancellationToken cancellationToken) =>
        AnswerAsync(tankId, ownerProfileId, followerProfileId, FollowStatus.Pending, approve: true, "tank.follow_approved", RequestNotFoundCode, cancellationToken);

    /// <summary>
    /// Declines an open follow request of the owner's tank. The request is removed, and the member can send a
    /// new request.
    /// </summary>
    public Task<string?> DeclineAsync(long tankId, long ownerProfileId, long followerProfileId, CancellationToken cancellationToken) =>
        AnswerAsync(tankId, ownerProfileId, followerProfileId, FollowStatus.Pending, approve: false, "tank.follow_declined", RequestNotFoundCode, cancellationToken);

    /// <summary>
    /// Removes an approved follower from the owner's tank.
    /// </summary>
    public Task<string?> RemoveFollowerAsync(long tankId, long ownerProfileId, long followerProfileId, CancellationToken cancellationToken) =>
        AnswerAsync(tankId, ownerProfileId, followerProfileId, FollowStatus.Approved, approve: false, "tank.follower_removed", FollowerNotFoundCode, cancellationToken);

    /// <summary>
    /// The numbers of followers, open requests and selected members of the owner's tank.
    /// </summary>
    public async Task<TankAudienceDto> AudienceAsync(long tankId, CancellationToken cancellationToken) => new(
        await context.Follows.CountAsync(f => f.TargetTankId == tankId && f.Status == FollowStatus.Approved, cancellationToken),
        await context.Follows.CountAsync(f => f.TargetTankId == tankId && f.Status == FollowStatus.Pending, cancellationToken),
        await context.TankSelectedMembers.CountAsync(s => s.TankId == tankId, cancellationToken));

    private async Task<string?> AnswerAsync(
        long tankId, long ownerProfileId, long followerProfileId, FollowStatus expected, bool approve, string eventType, string missingCode,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tank = await LockTankAsync(tankId, cancellationToken);
        if (tank is null || tank.ProfileId != ownerProfileId)
        {
            return NotFoundCode;
        }

        var follow = await context.Follows.SingleOrDefaultAsync(
            f => f.TargetTankId == tankId && f.FollowerProfileId == followerProfileId && f.Status == expected, cancellationToken);
        if (follow is null)
        {
            return missingCode;
        }

        var now = DateTimeOffset.UtcNow;
        if (approve)
        {
            follow.Status = FollowStatus.Approved;
            follow.DecidedAt = now;
        }
        else
        {
            context.Follows.Remove(follow);
        }

        AddEvent(eventType, tank, followerProfileId, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private Task<bool> OwnsAsync(long tankId, long ownerProfileId, CancellationToken cancellationToken) =>
        context.Tanks.AsNoTracking().AnyAsync(t => t.Id == tankId && t.ProfileId == ownerProfileId && t.DeletedAt == null, cancellationToken);

    private async Task<Tank?> LockTankAsync(long tankId, CancellationToken cancellationToken)
    {
        var tank = await context.Tanks.FromSqlInterpolated($"SELECT * FROM public.tank WHERE id = {tankId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        return tank is null || tank.DeletedAt is not null ? null : tank;
    }

    private void AddEvent(string eventType, Tank tank, long memberProfileId, DateTimeOffset now) =>
        context.OutboxEvents.Add(OutboxEvent.Create(
            eventType, "tank", tank.Id.ToString(),
            JsonSerializer.Serialize(new { tankId = tank.Id, ownerProfileId = tank.ProfileId, memberProfileId }, PayloadOptions), now));
}
