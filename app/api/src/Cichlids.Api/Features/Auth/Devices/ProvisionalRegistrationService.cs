using System.Security.Claims;
using System.Text.Json;
using Cichlids.Api.Features.Streaks;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Api.Features.Auth.Devices;

public enum ProvisionalRegistrationFailure
{
    /// <summary>The device proof is missing, malformed, used, expired, wrongly signed or names no active credential of a provisional profile.</summary>
    ProofRefused,

    /// <summary>The Keycloak account already has a profile, so the content can only be moved into it.</summary>
    AccountHasProfile,

    /// <summary>The Keycloak account has no profile, so the provisional profile can only become it.</summary>
    AccountHasNoProfile,

    /// <summary>The member profile of the Keycloak account is suspended.</summary>
    AccountSuspended,
}

/// <summary>The value of a registration step or the reason it was refused.</summary>
public sealed class ProvisionalRegistrationResult<T>
{
    public T? Value { get; private init; }

    public ProvisionalRegistrationFailure? Failure { get; private init; }

    public static ProvisionalRegistrationResult<T> Success(T value) => new() { Value = value };

    public static ProvisionalRegistrationResult<T> Fail(ProvisionalRegistrationFailure failure) => new() { Failure = failure };
}

/// <summary>
/// Hands the provisional profile of an installation to the Keycloak account of the caller. Every
/// step needs two proofs in one request: the Keycloak identity of the caller and a signed challenge
/// of an active device credential of the provisional profile; each alone is refused. Upgrade links the
/// account to the provisional profile, which becomes a member with its first public handle and keeps all
/// its content. Move transfers the content into the existing member profile of the account in one
/// transaction and deletes the provisional profile. Both lock the provisional profile row and its
/// credentials in the order the cleanup of abandoned profiles locks them, so an upgrade or move and a
/// cleanup of the same profile exclude each other, and both retire every credential of the profile.
/// The unique oidc identity decides between parallel upgrades of one account.
/// </summary>
public sealed class ProvisionalRegistrationService(
    CichlidsDbContext context,
    DeviceAuthService deviceAuth,
    StreakService streaks,
    CurrentProfileService profiles,
    Lazy<GeneratedNames> generatedNames,
    TimeProvider clock,
    ILogger<ProvisionalRegistrationService> logger)
{
    private const string OidcProvider = "oidc";
    private const int MaxUpgradeAttempts = 3;

    /// <summary>
    /// What registering would do with the content of the provisional profile. Nothing is changed and
    /// no profile is created for the account.
    /// </summary>
    public async Task<ProvisionalRegistrationResult<ProvisionalPreviewDto>> PreviewAsync(
        ClaimsPrincipal user, DeviceTokenRequest proofRequest, DateOnly today, int utcOffsetMinutes, CancellationToken cancellationToken)
    {
        if (await deviceAuth.VerifyProofAsync(proofRequest, cancellationToken) is not { } proof)
        {
            return ProvisionalRegistrationResult<ProvisionalPreviewDto>.Fail(ProvisionalRegistrationFailure.ProofRefused);
        }

        var member = await profiles.FindMemberAsync(user, cancellationToken);
        if (member is not null && IsSuspended(member))
        {
            return ProvisionalRegistrationResult<ProvisionalPreviewDto>.Fail(ProvisionalRegistrationFailure.AccountSuspended);
        }

        var counts = await CountAsync(proof.ProfileId, cancellationToken);
        var plan = member is null ? null : await PlanVacationsAsync(proof.ProfileId, member.Id, today, utcOffsetMinutes, cancellationToken);
        return ProvisionalRegistrationResult<ProvisionalPreviewDto>.Success(new ProvisionalPreviewDto(
            member is null ? ProvisionalRegistrationMode.Upgrade : ProvisionalRegistrationMode.Move,
            counts with { Vacations = plan?.Moved.Count ?? counts.Vacations },
            plan?.Overlapping.Count ?? 0,
            plan?.Repairing.Count ?? 0,
            member?.DisplayName));
    }

    /// <summary>
    /// Links the Keycloak account of the caller to the provisional profile and turns the profile into
    /// a member. Returns the id of the profile.
    /// </summary>
    public async Task<ProvisionalRegistrationResult<long>> UpgradeAsync(
        ClaimsPrincipal user, DeviceTokenRequest proofRequest, CancellationToken cancellationToken)
    {
        if (await deviceAuth.VerifyProofAsync(proofRequest, cancellationToken) is not { } proof)
        {
            return ProvisionalRegistrationResult<long>.Fail(ProvisionalRegistrationFailure.ProofRefused);
        }

        var subject = user.KeycloakSubject() ?? throw new RegistrationRequiredException();
        for (var attempt = 1; attempt <= MaxUpgradeAttempts; attempt++)
        {
            try
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                if (!await LockProvisionalAsync(proof, cancellationToken))
                {
                    return ProvisionalRegistrationResult<long>.Fail(ProvisionalRegistrationFailure.ProofRefused);
                }

                if (await context.ProfileIdentities.AnyAsync(i => i.Provider == OidcProvider && i.Subject == subject, cancellationToken))
                {
                    return ProvisionalRegistrationResult<long>.Fail(ProvisionalRegistrationFailure.AccountHasProfile);
                }

                var (username, displayName) = await MemberNames.ResolveAsync(
                    context, generatedNames.Value, user, subject, cancellationToken);
                var now = clock.GetUtcNow();
                var profile = await context.Profiles.SingleAsync(p => p.Id == proof.ProfileId, cancellationToken);
                profile.Username = username;
                profile.DisplayName = displayName;
                profile.Kind = ProfileKind.Member;
                profile.Identities.Add(new ProfileIdentity { Provider = OidcProvider, Subject = subject, CreatedAt = now });
                await RetireCredentialsAsync(profile.Id, now, cancellationToken);
                context.OutboxEvents.Add(OutboxEvent.Create(
                    "profile.registered", "profile", profile.Id.ToString(),
                    JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["profileId"] = profile.Id, ["registeredAt"] = now, ["movedContent"] = false,
                    }), now));
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                logger.LogInformation("Provisional profile {ProfileId} became a member.", profile.Id);
                return ProvisionalRegistrationResult<long>.Success(profile.Id);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A parallel call took the account or the handle; the next pass sees the account's profile or picks another handle.
                context.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException($"The provisional profile could not become a member after {MaxUpgradeAttempts} attempts.");
    }

    /// <summary>
    /// Moves the content of the provisional profile into the member profile of the Keycloak account
    /// of the caller and deletes the provisional profile.
    /// </summary>
    public async Task<ProvisionalRegistrationResult<MoveSummary>> MoveAsync(
        ClaimsPrincipal user, DeviceTokenRequest proofRequest, DateOnly today, int utcOffsetMinutes, CancellationToken cancellationToken)
    {
        if (await deviceAuth.VerifyProofAsync(proofRequest, cancellationToken) is not { } proof)
        {
            return ProvisionalRegistrationResult<MoveSummary>.Fail(ProvisionalRegistrationFailure.ProofRefused);
        }

        var target = await profiles.FindMemberAsync(user, cancellationToken);
        if (target is null)
        {
            return ProvisionalRegistrationResult<MoveSummary>.Fail(ProvisionalRegistrationFailure.AccountHasNoProfile);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Moves into one member profile run one after the other; the member row is locked before the provisional row.
        var memberRows = await context.Database
            .SqlQuery<long>($"SELECT id AS \"Value\" FROM public.profile WHERE id = {target.Id} AND kind = 'member' FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (memberRows.Count == 0 || !await LockProvisionalAsync(proof, cancellationToken))
        {
            return ProvisionalRegistrationResult<MoveSummary>.Fail(ProvisionalRegistrationFailure.ProofRefused);
        }

        var suspendedUntil = await context.Profiles.AsNoTracking()
            .Where(p => p.Id == target.Id).Select(p => p.SuspendedUntil).SingleAsync(cancellationToken);
        if (suspendedUntil > clock.GetUtcNow())
        {
            return ProvisionalRegistrationResult<MoveSummary>.Fail(ProvisionalRegistrationFailure.AccountSuspended);
        }

        var now = clock.GetUtcNow();
        var from = proof.ProfileId;
        var into = target.Id;
        var counts = await CountAsync(from, cancellationToken);
        // The vacations are judged against the tanks of the member as they are before the tanks move in.
        var plan = await PlanVacationsAsync(from, into, today, utcOffsetMinutes, cancellationToken);

        await context.Tanks.Where(t => t.ProfileId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ProfileId, into), cancellationToken);
        await context.Inhabitants.Where(i => i.OwnerProfileId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.OwnerProfileId, into), cancellationToken);
        await context.Tanks.Where(t => t.DeletedByProfileId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedByProfileId, into), cancellationToken);
        await context.CareEntries.Where(e => e.AuthorProfileId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.AuthorProfileId, into), cancellationToken);
        await context.CareTodos.Where(t => t.ProfileId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ProfileId, into), cancellationToken);
        await context.CareReminderSnoozes.Where(x => x.ProfileId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProfileId, into), cancellationToken);
        await context.WaterChangeSessions.Where(x => x.ProfileId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProfileId, into), cancellationToken);
        await context.VideoUploads.Where(u => u.OwnerProfileId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.OwnerProfileId, into), cancellationToken);
        await context.Collections.Where(c => c.ProfileId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ProfileId, into), cancellationToken);

        // A photo the member already used as proof keeps the row of the member; the other rows move.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public.used_checksum u SET profile_id = {into}
            WHERE u.profile_id = {from} AND NOT EXISTS (
                SELECT 1 FROM public.used_checksum x
                WHERE x.profile_id = {into} AND x.checksum_sha256 = u.checksum_sha256 AND x.kind = u.kind)
            """, cancellationToken);

        // The owner of a media item decides who may open a signed link; the new generation ends the links the device holds.
        var media = context.MediaItems.Where(m => m.OwnerProfileId == from).Select(m => m.Id);
        await context.MarkForPlacementAsync(media, cancellationToken);
        await context.MediaItems.Where(m => m.OwnerProfileId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.OwnerProfileId, into), cancellationToken);

        foreach (var vacation in plan.Moved)
        {
            vacation.ProfileId = into;
        }

        context.Vacations.RemoveRange(plan.Overlapping.Concat(plan.Repairing));

        await RetireCredentialsAsync(from, now, cancellationToken, into);
        context.OutboxEvents.Add(OutboxEvent.Create(
            "profile.provisional_content_moved", "profile", into.ToString(),
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["profileId"] = into, ["movedAt"] = now, ["tanks"] = counts.Tanks, ["careEntries"] = counts.CareEntries,
                ["mediaItems"] = counts.MediaItems, ["todos"] = counts.Todos,
                ["vacations"] = plan.Moved.Count, ["vacationsDropped"] = plan.Overlapping.Count,
                ["vacationsDroppedRepair"] = plan.Repairing.Count,
            }), now));
        await context.SaveChangesAsync(cancellationToken);

        await ProvisionalContentRemoval.DeleteAsync(context, from, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("The content of provisional profile {ProfileId} moved into profile {TargetProfileId}.", from, into);
        return ProvisionalRegistrationResult<MoveSummary>.Success(new MoveSummary(
            into, counts with { Vacations = plan.Moved.Count }, plan.Overlapping.Count, plan.Repairing.Count));
    }

    private bool IsSuspended(Profile profile) => profile.SuspendedUntil > clock.GetUtcNow();

    /// <summary>
    /// Locks the provisional profile row and its credential rows in the order of the cleanup and
    /// confirms that the proven credential is still active and the profile still provisional. A
    /// profile the cleanup removed in the meantime has no row and the proof counts as refused.
    /// </summary>
    private async Task<bool> LockProvisionalAsync(DeviceProof proof, CancellationToken cancellationToken)
    {
        var rows = await context.Database
            .SqlQuery<long>($"SELECT id AS \"Value\" FROM public.profile WHERE id = {proof.ProfileId} AND kind = 'provisional' FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return false;
        }

        await context.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM public.device_credential WHERE profile_id = {proof.ProfileId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return await context.DeviceCredentials.AsNoTracking()
            .AnyAsync(c => c.Id == proof.CredentialId && c.ProfileId == proof.ProfileId && c.State == DeviceCredentialState.Active, cancellationToken);
    }

    /// <summary>
    /// Retires every credential of the profile, optionally handing the retired credentials to another
    /// profile, so the retired key stays known and cannot register again.
    /// </summary>
    private async Task RetireCredentialsAsync(long profileId, DateTimeOffset now, CancellationToken cancellationToken, long? handOverTo = null)
    {
        await context.DeviceCredentials.Where(c => c.ProfileId == profileId).ExecuteUpdateAsync(
            s => s.SetProperty(c => c.State, DeviceCredentialState.Retired)
                .SetProperty(c => c.RetiredAt, now)
                .SetProperty(c => c.ProfileId, c => handOverTo ?? c.ProfileId),
            cancellationToken);
    }

    private async Task<ProvisionalContentCounts> CountAsync(long profileId, CancellationToken cancellationToken) =>
        new(
            await context.Tanks.CountAsync(t => t.ProfileId == profileId && t.DeletedAt == null, cancellationToken),
            await context.CareEntries.CountAsync(e => e.AuthorProfileId == profileId, cancellationToken),
            await context.MediaItems.CountAsync(m => m.OwnerProfileId == profileId, cancellationToken),
            await context.CareTodos.CountAsync(t => t.ProfileId == profileId, cancellationToken),
            await context.Vacations.CountAsync(v => v.ProfileId == profileId, cancellationToken));

    /// <summary>
    /// Splits the vacations of the provisional profile into those that move, those that overlap
    /// a vacation of the member, which is kept, or one that moves before them, and those that would
    /// repair a period of a live tank of the member and start before the repair window: moving a
    /// vacation enters it for the tanks of the member. The tanks that move in are not part of that
    /// judgement, since the vacations were entered for them. An active vacation counts as lasting
    /// without end. The returned entities are tracked.
    /// </summary>
    private async Task<VacationPlan> PlanVacationsAsync(
        long fromProfileId, long memberProfileId, DateOnly today, int utcOffsetMinutes, CancellationToken cancellationToken)
    {
        var kept = await context.Vacations.AsNoTracking().Where(v => v.ProfileId == memberProfileId)
            .Select(v => new { v.StartDate, v.EndDate }).ToListAsync(cancellationToken);
        var ranges = kept.Select(v => (v.StartDate, End: v.EndDate ?? DateOnly.MaxValue)).ToList();
        var moved = new List<Vacation>();
        var overlapping = new List<Vacation>();
        var repairing = new List<Vacation>();
        foreach (var vacation in await context.Vacations.Where(v => v.ProfileId == fromProfileId).OrderBy(v => v.StartDate).ThenBy(v => v.Id)
                     .ToListAsync(cancellationToken))
        {
            var end = vacation.EndDate ?? DateOnly.MaxValue;
            if (ranges.Any(r => r.StartDate <= end && vacation.StartDate <= r.End))
            {
                overlapping.Add(vacation);
                continue;
            }

            if (await streaks.WouldRepairAsync(memberProfileId, new VacationSpan(vacation.StartDate, vacation.EndDate), today, utcOffsetMinutes, cancellationToken))
            {
                repairing.Add(vacation);
                continue;
            }

            ranges.Add((vacation.StartDate, end));
            moved.Add(vacation);
        }

        return new VacationPlan(moved, overlapping, repairing);
    }

    private sealed record VacationPlan(List<Vacation> Moved, List<Vacation> Overlapping, List<Vacation> Repairing);
}

/// <summary>The member profile that received the content, the amounts that moved and the vacations that were dropped for overlap and for repairing.</summary>
public sealed record MoveSummary(long ProfileId, ProvisionalContentCounts Moved, int VacationsDropped, int VacationsDroppedRepair);
