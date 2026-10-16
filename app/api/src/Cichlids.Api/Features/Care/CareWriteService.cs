using Cichlids.Api.Features.Common;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

public sealed class CareWriteService(CichlidsDbContext context, TimeProvider clock, CareEntryMapper mapper,
    Media.MediaPlacementTrigger? placement = null)
{
    private const int MaxMeasurementValues = 10;
    private const int MaxNoteLength = 2000;

    // A blank note removes the note.
    private static string? NormalizeNote(string note) => note.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    public static string? ValidateAmount(CareAmountRequest? amount)
    {
        if (amount is null) return null;
        if (amount.Unit is not ("liter" or "us_gallon" or "percent")) return "care_request_invalid";
        return amount.Value <= 0 || amount.Value > (amount.Unit == "percent" ? 100m : 1000000m)
            ? "care_amount_invalid" : null;
    }

    /// <summary>
    /// Validates the values of a measurement and builds them with their normalized values. Returns
    /// the problem code of the first refusal.
    /// </summary>
    public static (List<CareMeasurementValue>? Values, string? Error) BuildValues(IReadOnlyList<MeasurementValueRequest>? requests)
    {
        if (requests is null || requests.Count == 0 || requests.Count > MaxMeasurementValues) return (null, "care_measurement_invalid");
        var values = new List<CareMeasurementValue>();
        foreach (var request in requests)
        {
            if (!SnakeCaseEnum.TryParse<CareParameter>(request.Parameter, out var parameter)) return (null, "care_parameter_invalid");
            var rule = CareMeasurements.RuleFor(parameter);
            var unit = rule.InputUnits[0];
            if (request.Unit is not null && (!SnakeCaseEnum.TryParse<MeasurementUnit>(request.Unit, out unit) || !rule.InputUnits.Contains(unit)))
                return (null, "care_unit_invalid");
            MeasurementOrigin? origin = null;
            if (request.Origin is not null)
            {
                if (!SnakeCaseEnum.TryParse<MeasurementOrigin>(request.Origin, out var parsed)) return (null, "care_origin_invalid");
                origin = parsed;
            }
            var normalized = CareMeasurements.Normalize(parameter, request.Value, unit);
            if (!CareMeasurements.IsPlausible(parameter, normalized)) return (null, "care_value_invalid");
            if (values.Any(v => v.Parameter == parameter)) return (null, "care_measurement_invalid");
            values.Add(new CareMeasurementValue { Parameter = parameter, InputValue = request.Value, InputUnit = unit, NormalizedValue = normalized, Origin = origin });
        }
        return (values, null);
    }

    public async Task<(CareEntryDto? Entry, string? Error)> LogAsync(long tankId, long profileId, LogCareRequest request, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await LockOwnTankAsync(tankId, profileId, ct)) return (null, "care_not_found");
        if (!SnakeCaseEnum.TryParse<CareKind>(request.Kind, out var kind)) return (null, "care_kind_invalid");
        var now = AtMicrosecondPrecision(clock.GetUtcNow());
        if (request.OccurredAt > now.AddMinutes(2)) return (null, "care_time_invalid");
        var entry = new CareEntry
        {
            TankId = tankId, AuthorProfileId = profileId, Kind = kind,
            OccurredAt = AtMicrosecondPrecision((request.OccurredAt ?? now).ToUniversalTime()), RecordedAt = now,
            OnlyMe = request.OnlyMe,
        };
        if (kind == CareKind.Measurement)
        {
            if (request.Amount is not null) return (null, "care_request_invalid");
            var (values, valuesError) = BuildValues(request.Values);
            if (valuesError is not null) return (null, valuesError);
            entry.Values = values!;
        }
        else
        {
            if (request.Values is not null) return (null, "care_measurement_invalid");
            if (ValidateAmount(request.Amount) is { } error) return (null, error);
            SetAmount(entry, request.Amount);
        }
        context.CareEntries.Add(entry);
        await context.SaveChangesAsync(ct);
        CareEvents.Add(context, "care.logged", entry, now);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (mapper.ToDto(entry), null);
    }

    public async Task<(CareEntryDto? Entry, string? Error)> ChangeAsync(long tankId, long id, long profileId,
        UpdateCareRequest? request, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await LockOwnTankAsync(tankId, profileId, ct)) return (null, "care_not_found");
        var entry = await CareEntryMapper.WithDetails(context.CareEntries)
            .SingleOrDefaultAsync(e => e.Id == id && e.TankId == tankId && e.AuthorProfileId == profileId, ct);
        if (entry is null) return (null, "care_not_found");
        var onlyMeChanged = false;
        if (request is not null)
        {
            var noteChange = request.Note is null ? null : NormalizeNote(request.Note);
            if (noteChange?.Length > MaxNoteLength) return (null, "care_note_invalid");
            if (request.OccurredAt > AtMicrosecondPrecision(clock.GetUtcNow()).AddMinutes(2)) return (null, "care_time_invalid");
            if (entry.WaterChangeSessionId is not null && request.OccurredAt is { } requested
                && AtMicrosecondPrecision(requested.ToUniversalTime()) != entry.OccurredAt) return (null, "care_time_fixed");
            if (request is { Amount: null, Values: null } && (request.Note is not null || request.OccurredAt is not null || request.OnlyMe is not null))
            {
                if (request.Note is not null) entry.Note = noteChange;
            }
            else
            {
                if (entry.Kind == CareKind.Measurement)
                {
                    if (request.Amount is not null) return (null, "care_request_invalid");
                    var (values, valuesError) = BuildValues(request.Values);
                    if (valuesError is not null) return (null, valuesError);
                    ReplaceValues(entry, values!);
                }
                else
                {
                    if (request.Values is not null) return (null, "care_measurement_invalid");
                    if (ValidateAmount(request.Amount) is { } error) return (null, error);
                    SetAmount(entry, request.Amount);
                }
                if (request.Note is not null) entry.Note = noteChange;
            }
            if (request.OnlyMe is { } onlyMe)
            {
                onlyMeChanged = onlyMe != entry.OnlyMe;
                entry.OnlyMe = onlyMe;
            }
            if (request.OccurredAt is { } occurredAt) entry.OccurredAt = AtMicrosecondPrecision(occurredAt.ToUniversalTime());
        }
        await mapper.PrepareAsync([entry], profileId, ct);
        var dto = mapper.ToDto(entry);
        CareEvents.Add(context, request is null ? "care.deleted" : "care.updated", entry, clock.GetUtcNow());
        var mediaItemIds = new List<long>();
        if (request is null)
        {
            // Deleting the entry deletes its links; the photos and videos stay in the library of the author.
            mediaItemIds = entry.MediaLinks.Select(l => l.MediaItemId).ToList();
            foreach (var todo in entry.Todos)
            {
                todo.CareEntryId = null;
                TodoEvents.Add(context, "todo.updated", todo, clock.GetUtcNow());
            }
            context.CareEntries.Remove(entry);
        }
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (placement is not null)
        {
            if (onlyMeChanged) await placement.PlaceNowAsync(await context.MediaItemIdsOfCareEntry(entry.Id).ToListAsync(ct));
            if (mediaItemIds.Count > 0) await placement.PlaceNowAsync(mediaItemIds);
        }

        if (request is not null) await mapper.PrepareAsync([entry], profileId, ct);
        return (request is null ? dto : mapper.ToDto(entry), null);
    }

    private Task<bool> LockOwnTankAsync(long tankId, long profileId, CancellationToken ct) =>
        CareTanks.LockOwnAsync(context, tankId, profileId, ct);

    internal static DateTimeOffset AtMicrosecondPrecision(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));

    private static void ReplaceValues(CareEntry entry, List<CareMeasurementValue> values)
    {
        foreach (var stale in entry.Values.Where(v => values.All(n => n.Parameter != v.Parameter)).ToList()) entry.Values.Remove(stale);
        foreach (var value in values)
        {
            var existing = entry.Values.SingleOrDefault(v => v.Parameter == value.Parameter);
            if (existing is null) entry.Values.Add(value);
            else
            {
                existing.InputValue = value.InputValue;
                existing.InputUnit = value.InputUnit;
                existing.NormalizedValue = value.NormalizedValue;
                existing.Origin = value.Origin;
            }
        }
    }

    internal static void SetAmount(CareEntry entry, CareAmountRequest? amount)
    {
        entry.AmountInputValue = amount?.Value;
        entry.AmountInputUnit = amount?.Unit switch
        {
            "liter" => CareAmountUnit.Liter, "us_gallon" => CareAmountUnit.UsGallon,
            "percent" => CareAmountUnit.Percent, _ => null,
        };
        entry.AmountLiters = amount?.Unit switch
        {
            "liter" => amount.Value, "us_gallon" => amount.Value * TankMeasurements.LitersPerUsGallon, _ => null,
        };
    }
}
