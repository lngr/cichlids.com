using System.Globalization;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// How a request to change the public handle ended.
/// </summary>
public enum HandleChangeOutcome
{
    Changed,
    Unchanged,
    Invalid,
    Generated,
    Reserved,
    Taken,
    TooSoon,
}

/// <summary>
/// The outcome of a handle change together with the handle the profile has afterwards and the
/// earliest time of the next change.
/// </summary>
public sealed record HandleChangeResult(HandleChangeOutcome Outcome, string? Handle, DateTimeOffset? NextChangeAt);

/// <summary>
/// Changes the public handle of a registered member. A member changes the handle once per 30 days.
/// The handle a member gives up stays reserved for that member for 90 days, so nobody else takes it
/// and the member may take it back. The profile row is locked for the whole change; the unique
/// index on the lowercase handle decides between members who ask for the same handle at once. The
/// outbox event profile.handle_changed commits with the change.
/// </summary>
public sealed class HandleChangeService(CichlidsDbContext context, TimeProvider clock)
{
    public const string InvalidCode = "handle_invalid";
    public const string GeneratedCode = "handle_generated";
    public const string ReservedCode = "handle_reserved";
    public const string TakenCode = "handle_taken";
    public const string TooSoonCode = "handle_change_too_soon";

    public static readonly TimeSpan ChangeInterval = TimeSpan.FromDays(30);
    public static readonly TimeSpan ReservationPeriod = TimeSpan.FromDays(90);

    /// <summary>
    /// The earliest time the member may change the handle, or null when a change is possible now.
    /// </summary>
    public static DateTimeOffset? NextChangeAt(Profile profile, DateTimeOffset now) =>
        profile.HandleChangedAt is { } changedAt && changedAt + ChangeInterval > now ? changedAt + ChangeInterval : null;

    public async Task<HandleChangeResult> ChangeAsync(long profileId, string? requested, CancellationToken cancellationToken)
    {
        var verdict = HandleRules.Check(requested);
        switch (verdict.Check)
        {
            case HandleCheck.Invalid:
                return new HandleChangeResult(HandleChangeOutcome.Invalid, null, null);
            case HandleCheck.Generated:
                return new HandleChangeResult(HandleChangeOutcome.Generated, null, null);
            case HandleCheck.Reserved:
                return new HandleChangeResult(HandleChangeOutcome.Reserved, null, null);
        }

        var handle = verdict.Handle;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM public.profile WHERE id = {profileId} FOR UPDATE", cancellationToken);
        // The caller's profile is tracked from resolving the caller; the row is read again under the lock.
        context.ChangeTracker.Clear();
        var profile = await context.Profiles.SingleAsync(p => p.Id == profileId, cancellationToken);
        var now = clock.GetUtcNow();

        if (profile.Username == handle)
        {
            return new HandleChangeResult(HandleChangeOutcome.Unchanged, profile.Username, NextChangeAt(profile, now));
        }

        if (NextChangeAt(profile, now) is { } next)
        {
            return new HandleChangeResult(HandleChangeOutcome.TooSoon, null, next);
        }

        if (await context.Profiles.AnyAsync(p => p.Id != profileId && p.Username.ToLower() == handle, cancellationToken)
            || await context.ReleasedHandles.AnyAsync(
                r => r.Handle == handle && r.ProfileId != profileId && r.ReservedUntil > now, cancellationToken))
        {
            return new HandleChangeResult(HandleChangeOutcome.Taken, null, null);
        }

        var previous = profile.Username;
        var previousKey = previous.ToLowerInvariant();
        await context.ReleasedHandles.Where(r => r.Handle == handle || r.Handle == previousKey).ExecuteDeleteAsync(cancellationToken);
        if (previousKey != handle)
        {
            context.ReleasedHandles.Add(new ReleasedHandle
            {
                ProfileId = profileId, Handle = previousKey, ReleasedAt = now, ReservedUntil = now + ReservationPeriod,
            });
        }

        profile.Username = handle;
        profile.HandleChangedAt = now;
        context.OutboxEvents.Add(OutboxEvent.Create(
            "profile.handle_changed", "profile", profileId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["profileId"] = profileId }), now));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new HandleChangeResult(HandleChangeOutcome.Taken, null, null);
        }

        await transaction.CommitAsync(cancellationToken);
        return new HandleChangeResult(HandleChangeOutcome.Changed, handle, now + ChangeInterval);
    }
}
