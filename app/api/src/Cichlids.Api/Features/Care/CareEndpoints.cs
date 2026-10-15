using System.Security.Claims;
using System.Text.RegularExpressions;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

public static partial class CareEndpoints
{
    /// <summary>
    /// Whether a request path addresses the care routes of a tank, so framework refusals such as
    /// binding errors or a missing login on these routes carry a care problem code.
    /// </summary>
    public static bool IsCarePath(PathString path) => path.HasValue && CarePath().IsMatch(path.Value!);

    [GeneratedRegex("^/api/tanks/[0-9]+/care(/[0-9]+|/series)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex CarePath();

    public static IEndpointRouteBuilder MapCareEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tanks/{tankId:long}/care").WithTags("Care");
        group.MapGet("", ListAsync).WithName("ListCareEntries").ProducesProblem(404);
        group.MapGet("/series", SeriesAsync).WithName("GetCareSeries").ProducesProblem(400).ProducesProblem(404);
        group.MapPost("", LogAsync).WithName("LogCareEntry").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        group.MapPatch("/{id:long}", UpdateAsync).WithName("UpdateCareEntry").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        group.MapDelete("/{id:long}", DeleteAsync).WithName("DeleteCareEntry").RequireAuthorization()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        return app;
    }

    private static async Task<bool> OwnsTankAsync(long tankId, ClaimsPrincipal user, CichlidsDbContext db, CancellationToken ct)
    {
        var subject = user.FindFirst("sub")?.Value;
        return user.Identity?.IsAuthenticated == true && await db.Tanks.AnyAsync(t => t.Id == tankId && t.DeletedAt == null &&
            db.ProfileIdentities.Any(i => i.ProfileId == t.ProfileId && i.Provider == "oidc" && i.Subject == subject), ct);
    }

    private static async Task<Results<Ok<PagedResponse<CareEntryDto>>, ProblemHttpResult>> ListAsync(
        long tankId, int? offset, int? limit, ClaimsPrincipal user, CichlidsDbContext db, CancellationToken ct)
    {
        if (!await OwnsTankAsync(tankId, user, db, ct)) return Problem("care_not_found");
        var (skip, take) = Pagination.Normalize(offset, limit);
        var query = db.CareEntries.AsNoTracking().Where(e => e.TankId == tankId);
        var total = await query.CountAsync(ct);
        var entries = await query.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Skip(skip).Take(take)
            .Include(e => e.Values).ToListAsync(ct);
        return TypedResults.Ok(new PagedResponse<CareEntryDto>(total, entries.Select(CareEntryDto.From).ToList()));
    }

    private const int MaxSeriesPoints = 1000;

    private static async Task<Results<Ok<CareSeriesDto>, ProblemHttpResult>> SeriesAsync(
        long tankId, string? parameter, [FromQuery(Name = "from")] DateTimeOffset? start, [FromQuery(Name = "to")] DateTimeOffset? end,
        ClaimsPrincipal user, CichlidsDbContext db, CancellationToken ct)
    {
        if (!await OwnsTankAsync(tankId, user, db, ct)) return Problem("care_not_found");
        if (!SnakeCaseEnum.TryParse<CareParameter>(parameter, out var kind)) return Problem("care_parameter_invalid");
        if (start > end) return Problem("care_range_invalid");
        var points = db.CareMeasurementValues.AsNoTracking().Where(v => v.Parameter == kind)
            .Join(db.CareEntries.Where(e => e.TankId == tankId && (start == null || e.OccurredAt >= start) && (end == null || e.OccurredAt <= end)),
                v => v.CareEntryId, e => e.Id, (v, e) => new { Value = v, Entry = e });
        var newest = await points.OrderByDescending(p => p.Entry.OccurredAt).ThenByDescending(p => p.Entry.Id).Take(MaxSeriesPoints)
            .Select(p => new CareSeriesPointDto(p.Entry.Id, p.Entry.OccurredAt, p.Value.NormalizedValue, p.Value.InputValue, p.Value.InputUnit, p.Value.Origin))
            .ToListAsync(ct);
        newest.Reverse();
        return TypedResults.Ok(new CareSeriesDto(kind, CareMeasurements.RuleFor(kind).CanonicalUnit, newest));
    }

    private static async Task<Results<Created<CareEntryDto>, ProblemHttpResult>> LogAsync(
        long tankId, LogCareRequest request, ClaimsPrincipal user, CurrentProfileService profiles, CareWriteService writes, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (entry, error) = await writes.LogAsync(tankId, profile.Id, request, ct);
        return error is not null ? Problem(error) : TypedResults.Created($"/api/tanks/{tankId}/care/{entry!.Id}", entry);
    }

    private static async Task<Results<Ok<CareEntryDto>, ProblemHttpResult>> UpdateAsync(
        long tankId, long id, UpdateCareRequest request, ClaimsPrincipal user, CurrentProfileService profiles, CareWriteService writes, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (entry, error) = await writes.ChangeAsync(tankId, id, profile.Id, request, ct);
        return error is not null ? Problem(error) : TypedResults.Ok(entry!);
    }

    private static async Task<Results<Ok<CareEntryDto>, ProblemHttpResult>> DeleteAsync(
        long tankId, long id, ClaimsPrincipal user, CurrentProfileService profiles, CareWriteService writes, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (entry, error) = await writes.ChangeAsync(tankId, id, profile.Id, null, ct);
        return error is not null ? Problem(error) : TypedResults.Ok(entry!);
    }

    private static ProblemHttpResult Problem(string code) => ApiProblems.WithCode(code == "care_not_found" ? 404 : 400,
        "Care request refused", code switch
        {
            "care_not_found" => "This care entry or tank is unavailable.",
            "care_time_invalid" => "The time must not be more than two minutes in the future.",
            "care_kind_invalid" => "Choose water_change or measurement.",
            "care_amount_invalid" => "Enter a positive amount, up to 100 percent or 1000000 volume units.",
            "care_measurement_invalid" => "A measurement needs one to ten values with distinct parameters, and a water change takes none.",
            "care_parameter_invalid" => "Choose temperature, ph, kh, gh, no2, no3, nh4, po4, conductivity or tds.",
            "care_unit_invalid" => "This unit does not apply to the parameter.",
            "care_value_invalid" => "The value is outside the plausible range of the parameter.",
            "care_origin_invalid" => "Choose test_kit, meter or probe.",
            "care_range_invalid" => "The start of the range must not be after its end.",
            _ => "Check the care request and its units.",
        }, code);
}
