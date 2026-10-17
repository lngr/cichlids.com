using System.Globalization;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Library;
using Cichlids.Api.Features.Media;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Fish;

/// <summary>
/// Writes the events and media of a named fish. Only the owner of the fish writes, and any other caller meets the
/// answer for a missing fish. A write locks the tank rows it touches in the order of their ids and then the fish
/// row, which serializes it with the writes of the stocking and with moves of other fish. Each write commits with
/// an outbox event that carries ids only. A move changes the tank of the fish and records the event in one
/// transaction; a death sets the removal day and the reason died and records the event in the same transaction. Days are
/// calendar dates of the caller, never after the local date of the caller and not before the day the fish
/// moved in. Problem codes: fish_not_found, fish_invalid, fish_date_invalid, fish_move_tank_invalid, fish_removed,
/// fish_event_fixed, media_not_found and media_already_linked.
/// </summary>
public sealed class FishWriteService(CichlidsDbContext context, MediaLibraryService library, TimeProvider clock, MediaPlacementTrigger? placement = null)
{
    public const string NotFoundCode = FishService.NotFoundCode;
    public const string InvalidCode = "fish_invalid";
    public const string DateInvalidCode = "fish_date_invalid";
    public const string MoveTankInvalidCode = "fish_move_tank_invalid";
    public const string RemovedCode = "fish_removed";
    public const string EventFixedCode = "fish_event_fixed";

    private const int LockAttempts = 3;
    private const decimal CentimetersPerInch = 2.54m;

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Records a measured length. The error is a problem code.
    /// </summary>
    public async Task<string?> RecordGrowthAsync(long id, long profileId, RecordGrowthRequest? request, DateOnly today, CancellationToken cancellationToken)
    {
        if (request?.Length is not { } length || length <= 0 || request.Unit is not { } unitText
            || !SnakeCaseEnum.TryParse<DimensionUnit>(unitText, out var unit)) return InvalidCode;
        var lengthCm = Math.Round(unit == DimensionUnit.Inch ? length * CentimetersPerInch : length, 3, MidpointRounding.AwayFromZero);
        if (lengthCm <= 0 || lengthCm > InhabitantEventConfiguration.MaxLengthCm || length >= 100000m) return InvalidCode;

        return await WriteAsync(id, profileId, null, async fish =>
        {
            if (fish.RemovedOn is not null) return RemovedCode;
            if (ResolveDay(request.On, today, fish) is not { } day) return DateInvalidCode;
            context.InhabitantEvents.Add(new InhabitantEvent
            {
                InhabitantId = fish.Id, Kind = InhabitantEventKind.Growth, OccurredOn = day, LengthCm = lengthCm, LengthInputValue = length, LengthInputUnit = unit,
            });
            AddOutbox("inhabitant.growth_recorded", fish);
            return null;
        }, cancellationToken);
    }

    /// <summary>
    /// Records a spawning with a pure-bred generation or a hybrid offspring. The error is a problem code.
    /// </summary>
    public async Task<string?> RecordSpawningAsync(long id, long profileId, RecordSpawningRequest? request, DateOnly today, CancellationToken cancellationToken)
    {
        var hybrid = request?.Hybrid == true;
        if (request is null || (hybrid ? request.Generation is not null : request.Generation is not (>= 1 and <= InhabitantEventConfiguration.MaxGeneration)))
            return InvalidCode;

        return await WriteAsync(id, profileId, null, async fish =>
        {
            if (fish.RemovedOn is not null) return RemovedCode;
            if (ResolveDay(request.On, today, fish) is not { } day) return DateInvalidCode;
            context.InhabitantEvents.Add(new InhabitantEvent
            {
                InhabitantId = fish.Id, Kind = InhabitantEventKind.Spawning, OccurredOn = day, OffspringGeneration = hybrid ? null : request.Generation, OffspringHybrid = hybrid,
            });
            AddOutbox("inhabitant.spawned", fish);
            return null;
        }, cancellationToken);
    }

    /// <summary>
    /// Moves the fish to another live tank of its owner and records the move. The error is a problem code.
    /// </summary>
    public async Task<string?> MoveAsync(long id, long profileId, MoveFishRequest? request, DateOnly today, CancellationToken cancellationToken)
    {
        if (request?.ToTankId is not { } target) return MoveTankInvalidCode;
        var error = await WriteAsync(id, profileId, target, async fish =>
        {
            if (fish.RemovedOn is not null) return RemovedCode;
            if (ResolveDay(request.On, today, fish) is not { } day) return DateInvalidCode;
            if (target == fish.TankId || !await context.Tanks.AsNoTracking().AnyAsync(t => t.Id == target && t.ProfileId == profileId && t.DeletedAt == null, cancellationToken))
                return MoveTankInvalidCode;
            var nextSort = (await context.Inhabitants.Where(i => i.TankId == target).MaxAsync(i => (int?)i.Sort, cancellationToken) ?? -1) + 1;
            context.InhabitantEvents.Add(new InhabitantEvent
            {
                InhabitantId = fish.Id, Kind = InhabitantEventKind.Moved, OccurredOn = day, FromTankId = fish.TankId, ToTankId = target,
            });
            fish.TankId = target;
            fish.Sort = nextSort;
            AddOutbox("inhabitant.moved", fish);
            return null;
        }, cancellationToken);
        if (error is null) await PlaceMediaAsync(id);
        return error;
    }

    /// <summary>
    /// Records the death of the fish: it leaves the stocking with the reason died and the page becomes a
    /// memorial. The day is not before the latest event of the fish. The error is a problem code.
    /// </summary>
    public async Task<string?> RecordDeathAsync(long id, long profileId, RecordDeathRequest? request, DateOnly today, CancellationToken cancellationToken) =>
        await WriteAsync(id, profileId, null, async fish =>
        {
            if (fish.RemovedOn is not null) return RemovedCode;
            if (ResolveDay(request?.On, today, fish) is not { } day) return DateInvalidCode;
            if (await context.InhabitantEvents.AnyAsync(e => e.InhabitantId == fish.Id && e.OccurredOn > day, cancellationToken)) return DateInvalidCode;
            fish.RemovedOn = day;
            fish.RemovalReason = InhabitantRemovalReason.Died;
            context.InhabitantEvents.Add(new InhabitantEvent { InhabitantId = fish.Id, Kind = InhabitantEventKind.Died, OccurredOn = day });
            AddOutbox("inhabitant.removed", fish);
            return null;
        }, cancellationToken);

    /// <summary>
    /// Deletes a growth or spawning entry of the fish. A move and the death are facts of the fish and stay.
    /// </summary>
    public async Task<string?> DeleteEventAsync(long id, long profileId, long eventId, CancellationToken cancellationToken) =>
        await WriteAsync(id, profileId, null, async fish =>
        {
            var stored = await context.InhabitantEvents.SingleOrDefaultAsync(e => e.Id == eventId && e.InhabitantId == fish.Id, cancellationToken);
            if (stored is null) return NotFoundCode;
            if (stored.Kind is not (InhabitantEventKind.Growth or InhabitantEventKind.Spawning)) return EventFixedCode;
            context.InhabitantEvents.Remove(stored);
            AddOutbox("inhabitant.updated", fish);
            return null;
        }, cancellationToken);

    /// <summary>
    /// Shows a file of the media library of the owner on the page of the fish. The error is a problem code.
    /// </summary>
    public async Task<string?> LinkMediaAsync(long id, long profileId, LinkFishMediaRequest? request, CancellationToken cancellationToken)
    {
        if (request?.MediaItemId is not { } mediaItemId) return await OwnsAsync(id, profileId, cancellationToken) ? InvalidCode : NotFoundCode;
        string? error;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            if (!await OwnsAsync(id, profileId, cancellationToken)) return NotFoundCode;
            error = await library.LinkToInhabitantAsync(profileId, id, mediaItemId, cancellationToken);
            if (error is not null) return error;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (placement is not null) await placement.PlaceNowAsync([mediaItemId]);
        return null;
    }

    /// <summary>
    /// Removes the link of a file from the page of the fish; the file stays in the library.
    /// </summary>
    public async Task<string?> UnlinkMediaAsync(long id, long profileId, long mediaItemId, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(id, profileId, cancellationToken)) return NotFoundCode;
        var removed = await context.MediaLinks.Where(l => l.InhabitantId == id && l.MediaItemId == mediaItemId).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0) return MediaLibraryService.NotFoundCode;
        if (placement is not null) await placement.PlaceNowAsync([mediaItemId]);
        return null;
    }

    private Task<bool> OwnsAsync(long id, long profileId, CancellationToken cancellationToken) =>
        context.Inhabitants.AsNoTracking().AnyAsync(i => i.Id == id && i.Name != null && i.OwnerProfileId == profileId, cancellationToken);

    // Runs a change of the fish in a transaction that holds the lock of the fish. The change returns a problem
    // code to refuse, or null to commit.
    private async Task<string?> WriteAsync(long id, long profileId, long? extraTankId, Func<Inhabitant, Task<string?>> change, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var fish = await LockOwnAsync(id, profileId, extraTankId, cancellationToken);
        if (fish is null) return NotFoundCode;
        if (await change(fish) is { } error) return error;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    // Locks the tank rows of the fish (and of a target tank) in the order of their ids and then the fish row.
    // The stocking writes lock the tank row first, so the order is the same everywhere. A fish that another
    // transaction moved meanwhile is locked under its new tank on the next attempt.
    private async Task<Inhabitant?> LockOwnAsync(long id, long profileId, long? extraTankId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < LockAttempts; attempt++)
        {
            var tankId = await context.Inhabitants.AsNoTracking().Where(i => i.Id == id && i.Name != null && i.OwnerProfileId == profileId)
                .Select(i => (long?)i.TankId).SingleOrDefaultAsync(cancellationToken);
            if (tankId is null) return null;
            foreach (var locked in new[] { tankId.Value, extraTankId ?? tankId.Value }.Distinct().Order())
            {
                await context.Tanks.FromSqlInterpolated($"SELECT * FROM public.tank WHERE id = {locked} FOR UPDATE").AsNoTracking()
                    .Select(t => t.Id).ToListAsync(cancellationToken);
            }

            var fish = await context.Inhabitants.FromSqlInterpolated($"SELECT * FROM public.inhabitant WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
            if (fish is null || fish.OwnerProfileId != profileId) return null;
            if (fish.TankId == tankId) return fish;
            context.Entry(fish).State = EntityState.Detached;
        }

        return null;
    }

    private static DateOnly? ResolveDay(string? text, DateOnly today, Inhabitant fish)
    {
        var day = today;
        if (text is not null && !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) return null;
        return day <= today && (fish.MovedInOn is not { } movedIn || day >= movedIn) ? day : null;
    }

    private async Task PlaceMediaAsync(long inhabitantId)
    {
        if (placement is null) return;
        var ids = await context.MediaLinks.AsNoTracking().Where(l => l.InhabitantId == inhabitantId).Select(l => l.MediaItemId).ToListAsync();
        await placement.PlaceNowAsync(ids);
    }

    private void AddOutbox(string type, Inhabitant fish) =>
        context.OutboxEvents.Add(OutboxEvent.Create(type, "inhabitant", fish.Id.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { inhabitantId = fish.Id, tankId = fish.TankId, profileId = fish.OwnerProfileId, speciesId = fish.SpeciesId }, PayloadOptions),
            clock.GetUtcNow()));
}
