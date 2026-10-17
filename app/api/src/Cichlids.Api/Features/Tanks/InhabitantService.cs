using System.Globalization;
using System.Text.Json;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Common;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// Reads and writes the stocking of a tank. Everyone who may see the profile of a tank reads the stocking;
/// only the owner of a live tank writes, and any other caller meets the answer for a missing tank. Each
/// write commits with an outbox event (inhabitant.added, inhabitant.updated, inhabitant.removed) that
/// carries ids only. A name makes the count one. Removing a named fish with the reason died also records its
/// death on its timeline. Days are the calendar dates of the caller and never lie
/// after the local date of the caller.
/// </summary>
public sealed class InhabitantService(CichlidsDbContext context, TankAccessService access, TanksQueryService tanks, TimeProvider clock)
{
    public const string NotFoundCode = "inhabitant_not_found";
    public const string InvalidCode = "inhabitant_invalid";
    public const string SpeciesUnknownCode = "inhabitant_species_unknown";
    public const string MovedInInvalidCode = "inhabitant_moved_in_invalid";
    public const string ReasonInvalidCode = "inhabitant_reason_invalid";
    public const string RemovedOnInvalidCode = "inhabitant_removed_on_invalid";
    public const string RemovedCode = "inhabitant_removed";

    public const int MaxCount = 10000;

    private const string DateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The stocking of a tank for a viewer, or null when the viewer does not see the tank.
    /// </summary>
    public async Task<InhabitantListDto?> ListAsync(long tankId, long? viewerProfileId, CancellationToken ct)
    {
        if (await access.FindAsync(tankId, viewerProfileId, ct) is not { } found || !found.CanSeeProfile) return null;
        if (!found.IsOwner && !await tanks.IsVisibleAsync(tankId, viewerProfileId, ct)) return null;
        var rows = await context.Inhabitants.AsNoTracking().Where(i => i.TankId == tankId).ToListAsync(ct);
        var dtos = await ToDtosAsync(rows, ct);
        var current = rows.Where(r => r.RemovedOn is null).OrderBy(r => r.Name is null).ThenBy(r => r.Sort).ThenBy(r => r.Id).Select(r => dtos[r.Id]).ToList();
        var history = rows.Where(r => r.RemovedOn is not null).OrderByDescending(r => r.RemovedOn).ThenByDescending(r => r.Id).Select(r => dtos[r.Id]).ToList();
        return new InhabitantListDto(current, history);
    }

    /// <summary>
    /// Adds an inhabitant to the live tank of the owner. The error is a problem code.
    /// </summary>
    public async Task<(InhabitantDto? Inhabitant, string? Error)> AddAsync(
        long tankId, long profileId, AddInhabitantRequest? request, DateOnly today, CancellationToken ct)
    {
        var (fields, error) = await ValidateAsync(request?.SpeciesId, request?.Count, request?.Name, ct);
        if (error is not null) return (null, error);
        var movedIn = today;
        if (request?.MovedInOn is { } text && !TryParseUpTo(text, today, out movedIn)) return (null, MovedInInvalidCode);

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, NotFoundCode);
        var nextSort = (await context.Inhabitants.Where(i => i.TankId == tankId).MaxAsync(i => (int?)i.Sort, ct) ?? -1) + 1;
        var inhabitant = new Inhabitant
        {
            TankId = tankId, OwnerProfileId = profileId, SpeciesId = fields!.SpeciesId, Count = fields.Count, Name = fields.Name,
            MovedInOn = movedIn, Sort = nextSort,
        };
        context.Inhabitants.Add(inhabitant);
        await context.SaveChangesAsync(ct);
        AddEvent("inhabitant.added", inhabitant);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ((await ToDtosAsync([inhabitant], ct))[inhabitant.Id], null);
    }

    /// <summary>
    /// Changes the species, count, name and moved-in day of a current inhabitant of the owner.
    /// </summary>
    public async Task<(InhabitantDto? Inhabitant, string? Error)> UpdateAsync(
        long tankId, long id, long profileId, UpdateInhabitantRequest? request, DateOnly today, CancellationToken ct)
    {
        var (fields, error) = await ValidateAsync(request?.SpeciesId, request?.Count, request?.Name, ct);
        if (error is not null) return (null, error);
        DateOnly? movedIn = null;
        if (request?.MovedInOn is { } text)
        {
            if (!TryParseUpTo(text, today, out var parsed)) return (null, MovedInInvalidCode);
            movedIn = parsed;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, NotFoundCode);
        var inhabitant = await context.Inhabitants.SingleOrDefaultAsync(i => i.Id == id && i.TankId == tankId, ct);
        if (inhabitant is null) return (null, NotFoundCode);
        if (inhabitant.RemovedOn is not null) return (null, RemovedCode);
        inhabitant.SpeciesId = fields!.SpeciesId;
        inhabitant.Count = fields.Count;
        inhabitant.Name = fields.Name;
        if (movedIn is { } day) inhabitant.MovedInOn = day;
        AddEvent("inhabitant.updated", inhabitant);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ((await ToDtosAsync([inhabitant], ct))[inhabitant.Id], null);
    }

    /// <summary>
    /// Removes a current inhabitant of the owner from the stocking with a reason. The removal day is
    /// not before the moved-in day, or for a migrated inhabitant not before the day the tank was created.
    /// </summary>
    public async Task<(InhabitantDto? Inhabitant, string? Error)> RemoveAsync(
        long tankId, long id, long profileId, RemoveInhabitantRequest? request, DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        if (request?.Reason is not { } reasonText || !SnakeCaseEnum.TryParse<InhabitantRemovalReason>(reasonText, out var reason)) return (null, ReasonInvalidCode);
        var removedOn = today;
        if (request.RemovedOn is { } text && !TryParseUpTo(text, today, out removedOn)) return (null, RemovedOnInvalidCode);

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, NotFoundCode);
        var inhabitant = await context.Inhabitants.SingleOrDefaultAsync(i => i.Id == id && i.TankId == tankId, ct);
        if (inhabitant is null) return (null, NotFoundCode);
        if (inhabitant.RemovedOn is not null) return (null, RemovedCode);
        var presentSince = inhabitant.MovedInOn
            ?? CarePlans.LocalDate(await context.Tanks.Where(t => t.Id == tankId).Select(t => t.CreatedAt).SingleAsync(ct), utcOffsetMinutes);
        if (removedOn < presentSince) return (null, RemovedOnInvalidCode);
        if (reason == InhabitantRemovalReason.Died && inhabitant.Name is not null)
        {
            // The death of a named fish is an event on its timeline and does not precede an earlier event.
            if (await context.InhabitantEvents.AnyAsync(e => e.InhabitantId == id && e.OccurredOn > removedOn, ct)) return (null, RemovedOnInvalidCode);
            context.InhabitantEvents.Add(new InhabitantEvent { InhabitantId = id, Kind = InhabitantEventKind.Died, OccurredOn = removedOn });
        }

        inhabitant.RemovedOn = removedOn;
        inhabitant.RemovalReason = reason;
        AddEvent("inhabitant.removed", inhabitant);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ((await ToDtosAsync([inhabitant], ct))[inhabitant.Id], null);
    }

    private sealed record Fields(long? SpeciesId, int Count, string? Name);

    private async Task<(Fields? Fields, string? Error)> ValidateAsync(long? speciesId, int? count, string? requestName, CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(requestName) ? null : requestName.Trim();
        if (requestName is not null && name is null) return (null, InvalidCode);
        if (name is { Length: > InhabitantConfiguration.NameMaxLength }) return (null, InvalidCode);
        if (name is null && (speciesId is null || count is null || count < 1 || count > MaxCount)) return (null, InvalidCode);
        if (speciesId is { } id && !await context.Species.AnyAsync(s => s.Id == id, ct)) return (null, SpeciesUnknownCode);
        return (new Fields(speciesId, name is null ? count!.Value : 1, name), null);
    }

    private static bool TryParseUpTo(string text, DateOnly today, out DateOnly day) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day) && day <= today;

    private async Task<Dictionary<long, InhabitantDto>> ToDtosAsync(IReadOnlyCollection<Inhabitant> rows, CancellationToken ct) =>
        await InhabitantMapper.ToDtosAsync(context, rows, ct);

    private void AddEvent(string type, Inhabitant inhabitant) =>
        context.OutboxEvents.Add(OutboxEvent.Create(type, "inhabitant", inhabitant.Id.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { inhabitantId = inhabitant.Id, tankId = inhabitant.TankId, profileId = inhabitant.OwnerProfileId, speciesId = inhabitant.SpeciesId },
                PayloadOptions), clock.GetUtcNow()));
}
