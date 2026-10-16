using System.Globalization;
using System.Text.Json;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Streaks;

/// <summary>
/// Starts, ends and records the vacations of a member, which pause the streaks of all tanks of the
/// member. A member has at most one active vacation and no two vacations overlap. The outbox events
/// vacation.started and vacation.ended commit with the change.
/// </summary>
public sealed class VacationService(CichlidsDbContext context, TimeProvider clock)
{
    public const string NotFoundCode = "vacation_not_found";
    public const string DatesInvalidCode = "vacation_dates_invalid";
    public const string ActiveCode = "vacation_active";
    public const string OverlapCode = "vacation_overlap";

    public async Task<IReadOnlyList<VacationDto>> ListAsync(long profileId, CancellationToken ct) =>
        (await context.Vacations.AsNoTracking().Where(v => v.ProfileId == profileId).OrderByDescending(v => v.StartDate).ThenByDescending(v => v.Id)
            .ToListAsync(ct)).Select(ToDto).ToList();

    /// <summary>
    /// Starts an active vacation, or records an ended one when the request has an end date. Neither
    /// date lies after today.
    /// </summary>
    public async Task<(VacationDto? Vacation, string? Error)> StartAsync(long profileId, StartVacationRequest? request, DateOnly today, CancellationToken ct)
    {
        var (start, end, error) = ParseDates(request, today);
        if (error is not null) return (null, error);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await LockProfileAsync(profileId, ct);
        var existing = await context.Vacations.Where(v => v.ProfileId == profileId).ToListAsync(ct);
        if (end is null && existing.Any(v => v.EndDate is null)) return (null, ActiveCode);
        if (existing.Any(v => v.StartDate <= (end ?? DateOnly.MaxValue) && start <= (v.EndDate ?? DateOnly.MaxValue))) return (null, OverlapCode);
        var now = clock.GetUtcNow();
        var vacation = new Vacation { ProfileId = profileId, StartDate = start, EndDate = end, CreatedAt = now };
        context.Vacations.Add(vacation);
        await context.SaveChangesAsync(ct);
        AddEvent("vacation.started", vacation, now);
        if (end is not null) AddEvent("vacation.ended", vacation, now);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (ToDto(vacation), null);
    }

    /// <summary>
    /// Ends the active vacation on the given local date, which is not before its start.
    /// </summary>
    public async Task<(VacationDto? Vacation, string? Error)> EndAsync(long profileId, DateOnly today, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await LockProfileAsync(profileId, ct);
        var vacation = await context.Vacations.SingleOrDefaultAsync(v => v.ProfileId == profileId && v.EndDate == null, ct);
        if (vacation is null) return (null, NotFoundCode);
        if (today < vacation.StartDate) return (null, DatesInvalidCode);
        vacation.EndDate = today;
        AddEvent("vacation.ended", vacation, clock.GetUtcNow());
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (ToDto(vacation), null);
    }

    private static (DateOnly Start, DateOnly? End, string? Error) ParseDates(StartVacationRequest? request, DateOnly today)
    {
        DateOnly? start = null, end = null;
        if (request?.StartDate is { } startText)
        {
            if (!TryParse(startText, out var parsed)) return (default, null, DatesInvalidCode);
            start = parsed;
        }
        if (request?.EndDate is { } endText)
        {
            if (!TryParse(endText, out var parsed)) return (default, null, DatesInvalidCode);
            end = parsed;
            if (start is null) return (default, null, DatesInvalidCode);
        }
        var first = start ?? today;
        return first > today || (end is { } last && (last < first || last > today)) ? (default, null, DatesInvalidCode) : (first, end, null);
    }

    private static bool TryParse(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private Task LockProfileAsync(long profileId, CancellationToken ct) =>
        context.Profiles.FromSqlInterpolated($"SELECT * FROM public.profile WHERE id = {profileId} FOR UPDATE").ToListAsync(ct);

    private void AddEvent(string type, Vacation vacation, DateTimeOffset now) =>
        context.OutboxEvents.Add(OutboxEvent.Create(type, "vacation", vacation.Id.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["vacationId"] = vacation.Id, ["profileId"] = vacation.ProfileId, ["startDate"] = StreakService.Format(vacation.StartDate),
                ["endDate"] = vacation.EndDate is { } end ? StreakService.Format(end) : null, ["recordedAt"] = now,
            }), now));

    private static VacationDto ToDto(Vacation v) =>
        new(v.Id, StreakService.Format(v.StartDate), v.EndDate is { } end ? StreakService.Format(end) : null, v.EndDate is null);
}
