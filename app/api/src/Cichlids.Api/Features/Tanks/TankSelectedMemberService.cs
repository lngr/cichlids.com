using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// The members an owner names for a tank. A selected member needs no follow request and reaches the
/// levels selected and followers of the tank. The owner adds a member by the handle of the profile and
/// removes the member by the profile id. Every change locks the tank row and commits with its outbox
/// event. The error of a result is a problem code.
/// </summary>
public sealed class TankSelectedMemberService(CichlidsDbContext context, IObjectStore objectStore)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public const int MaxMembers = 100;
    public const string NotFoundCode = "tank_not_found";
    public const string HandleInvalidCode = "selected_member_handle_invalid";
    public const string ProfileNotFoundCode = "selected_member_profile_not_found";
    public const string OwnerCode = "selected_member_is_owner";
    public const string LimitCode = "selected_member_limit";
    public const string MemberNotFoundCode = "selected_member_not_found";

    /// <summary>
    /// The selected members of the owner's tank, in the order they were added, or null when the tank is not
    /// the caller's.
    /// </summary>
    public async Task<IReadOnlyList<TankMemberDto>?> ListAsync(long tankId, long ownerProfileId, CancellationToken cancellationToken)
    {
        if (!await context.Tanks.AsNoTracking().AnyAsync(t => t.Id == tankId && t.ProfileId == ownerProfileId && t.DeletedAt == null, cancellationToken))
        {
            return null;
        }

        var rows = await context.TankSelectedMembers.AsNoTracking().Where(s => s.TankId == tankId)
            .OrderBy(s => s.AddedAt).ThenBy(s => s.Id).Select(s => new { s.MemberProfileId, s.AddedAt }).ToListAsync(cancellationToken);
        var authors = await AuthorBatchLoader.LoadAsync(context, rows.Select(r => r.MemberProfileId).ToList(), cancellationToken);
        return rows.Select(r => new TankMemberDto(authors[r.MemberProfileId].ToDto(objectStore), r.AddedAt)).ToList();
    }

    /// <summary>
    /// Selects the member with the given handle for the owner's tank. Selecting a member who is selected already keeps the
    /// existing selection.
    /// </summary>
    public async Task<(TankMemberDto? Member, string? Error)> AddAsync(
        long tankId, long ownerProfileId, string? handle, CancellationToken cancellationToken)
    {
        var name = handle?.Trim().TrimStart('@');
        if (string.IsNullOrEmpty(name) || name.Length > 100)
        {
            return (null, HandleInvalidCode);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tank = await LockTankAsync(tankId, cancellationToken);
        if (tank is null || tank.ProfileId != ownerProfileId)
        {
            return (null, NotFoundCode);
        }

        var lowered = name.ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        // A suspended profile answers like a missing one, as it does on the public profile route.
        var member = await context.Profiles.AsNoTracking()
            .Where(p => p.Username.ToLower() == lowered && p.Kind == ProfileKind.Member && (p.SuspendedUntil == null || p.SuspendedUntil <= now))
            .Select(p => new { p.Id }).FirstOrDefaultAsync(cancellationToken);
        if (member is null)
        {
            return (null, ProfileNotFoundCode);
        }

        if (member.Id == ownerProfileId)
        {
            return (null, OwnerCode);
        }

        var existing = await context.TankSelectedMembers.AsNoTracking()
            .Where(s => s.TankId == tankId && s.MemberProfileId == member.Id).Select(s => (DateTimeOffset?)s.AddedAt).SingleOrDefaultAsync(cancellationToken);
        var addedAt = existing ?? DateTimeOffset.UtcNow;
        if (existing is null)
        {
            if (await context.TankSelectedMembers.CountAsync(s => s.TankId == tankId, cancellationToken) >= MaxMembers)
            {
                return (null, LimitCode);
            }

            context.TankSelectedMembers.Add(new TankSelectedMember { TankId = tankId, MemberProfileId = member.Id, AddedAt = addedAt });
            AddEvent("tank.selected_member_added", tank, member.Id, addedAt);
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var author = (await AuthorBatchLoader.LoadAsync(context, [member.Id], cancellationToken))[member.Id];
        return (new TankMemberDto(author.ToDto(objectStore), addedAt), null);
    }

    /// <summary>
    /// Removes a selected member from the owner's tank. A profile that is not selected has nothing to remove.
    /// </summary>
    public async Task<string?> RemoveAsync(long tankId, long ownerProfileId, long memberProfileId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tank = await LockTankAsync(tankId, cancellationToken);
        if (tank is null || tank.ProfileId != ownerProfileId)
        {
            return NotFoundCode;
        }

        var selected = await context.TankSelectedMembers.SingleOrDefaultAsync(
            s => s.TankId == tankId && s.MemberProfileId == memberProfileId, cancellationToken);
        if (selected is null)
        {
            return null;
        }

        context.TankSelectedMembers.Remove(selected);
        AddEvent("tank.selected_member_removed", tank, memberProfileId, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

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
