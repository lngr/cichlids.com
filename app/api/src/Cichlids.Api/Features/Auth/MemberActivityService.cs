using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Records when a registered member last used the API and when an absence of 28 days or more
/// began. Recording is one set-based statement that finds the profile through its oidc identity
/// and writes only when the stored time is missing or at least one hour old; the same statement
/// sets away_since to the previous last_seen_at when the gap reaches the absence threshold.
/// Concurrent requests of one member serialize on the row, and the loser of the race finds a
/// fresh last_seen_at and writes nothing, so away_since is written once per absence. The columns
/// are private to the member.
/// </summary>
public sealed class MemberActivityService(CichlidsDbContext context)
{
    /// <summary>
    /// Minimum age of last_seen_at before a request writes it again.
    /// </summary>
    public static readonly TimeSpan RecordInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Gap between two requests of a member from which the earlier one is the start of an absence.
    /// </summary>
    public static readonly TimeSpan AwayThreshold = TimeSpan.FromDays(28);

    private const string OidcProvider = "oidc";

    /// <summary>
    /// Records a request of the member behind the oidc subject at the given UTC time and tells
    /// whether the subject belongs to a profile of kind member. A subject without such a profile
    /// (a first login before the profile exists, an archived or system profile) matches nothing
    /// and yields false.
    /// </summary>
    public async Task<bool> RecordAsync(string subject, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var recordedBefore = now - RecordInterval;
        var awayBefore = now - AwayThreshold;

        // The update is a data-modifying CTE, which must be the top level statement and always runs
        // to completion. Its own filter is evaluated again against the committed row after a lock
        // wait, so concurrent requests of one member write once.
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = (NpgsqlCommand)context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                WITH recorded AS (
                    UPDATE public.profile AS p
                    SET away_since = CASE
                            WHEN p.last_seen_at IS NOT NULL AND p.last_seen_at <= @away_before THEN p.last_seen_at
                            ELSE p.away_since
                        END,
                        last_seen_at = @now
                    FROM public.profile_identity AS i
                    WHERE i.profile_id = p.id
                      AND i.provider = @provider
                      AND i.subject = @subject
                      AND p.kind = 'member'
                      AND (p.last_seen_at IS NULL OR p.last_seen_at <= @recorded_before)
                    RETURNING p.id
                )
                SELECT EXISTS (
                    SELECT 1
                    FROM public.profile_identity AS i
                    JOIN public.profile AS p ON p.id = i.profile_id
                    WHERE i.provider = @provider AND i.subject = @subject AND p.kind = 'member')
                """;
            command.Parameters.AddWithValue("away_before", awayBefore);
            command.Parameters.AddWithValue("recorded_before", recordedBefore);
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("provider", OidcProvider);
            command.Parameters.AddWithValue("subject", subject);
            return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// The start of the recorded absence of the profile, or null when none is recorded.
    /// </summary>
    public Task<DateTimeOffset?> GetAwaySinceAsync(long profileId, CancellationToken cancellationToken) =>
        context.Profiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => p.AwaySince)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Removes the recorded absence of the profile.
    /// </summary>
    public async Task ClearAwaySinceAsync(long profileId, CancellationToken cancellationToken) =>
        await context.Profiles.Where(p => p.Id == profileId && p.AwaySince != null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AwaySince, (DateTimeOffset?)null), cancellationToken);
}
