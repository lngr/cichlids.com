using System.Security.Claims;
using System.Text.RegularExpressions;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Tanks;
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
    /// Whether a request path addresses the care routes of a tank or the due care of a member, so framework refusals such as
    /// binding errors or a missing login on these routes carry a care problem code.
    /// </summary>
    public static bool IsCarePath(PathString path) => path.HasValue && CarePath().IsMatch(path.Value!);

    [GeneratedRegex("^(/api/tanks/[0-9]+/care(/[0-9]+(/photos(/[0-9]+)?|/videos/[0-9]+|/todos)?|/series|/plan|/plan/[a-z_]+)?|/api/tanks/[0-9]+/todos|/api/tanks/[0-9]+/water-changes(/current|/[0-9]+(/steps/[a-z_]+|/photos/[a-z_]+|/finish)?)?|/api/me/todos(/[0-9]+)?|/api/me/care/(due|reminders/snooze)|/api/care-photos/[^/]+|/api/care-videos/[^/]+/.+)/?$", RegexOptions.CultureInvariant)]
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
        group.MapGet("/plan", PlanAsync).WithName("GetCarePlan").ProducesProblem(400).ProducesProblem(404);
        group.MapPatch("/plan/{kind}", ChangeRuleAsync).WithName("UpdateCarePlanRule").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        group.MapPatch("/plan", ChangeRemindersAsync).WithName("UpdateCarePlan").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        app.MapCarePhotoEndpoints();
        app.MapWaterChangeEndpoints();
        app.MapTodoEndpoints();
        app.MapGet("/api/me/care/due", DueAsync).WithName("ListDueCare").WithTags("Care").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        return app;
    }

    /// <summary>
    /// The access of the caller to the care stream of a tank, or null when the caller does not see
    /// the stream or the tank does not exist; the two are not told apart.
    /// </summary>
    private static async Task<TankAccess?> StreamAccessAsync(long tankId, ClaimsPrincipal user, TankAccessService access, CancellationToken ct) =>
        await access.FindAsync(tankId, user, ct) is { CanSeeStream: true } found ? found : null;

    private static async Task<Results<Ok<PagedResponse<CareEntryDto>>, ProblemHttpResult>> ListAsync(
        long tankId, int? offset, int? limit, ClaimsPrincipal user, TankAccessService access, CichlidsDbContext db, CareEntryMapper mapper,
        CancellationToken ct)
    {
        if (await StreamAccessAsync(tankId, user, access, ct) is not { } stream) return Problem("care_not_found");
        var (skip, take) = Pagination.Normalize(offset, limit);
        var query = db.CareEntries.AsNoTracking().Where(e => e.TankId == tankId && (stream.IsOwner || !e.OnlyMe));
        var total = await query.CountAsync(ct);
        var entries = await CareEntryMapper.WithDetails(query.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Skip(skip).Take(take))
            .ToListAsync(ct);
        return TypedResults.Ok(new PagedResponse<CareEntryDto>(total, entries.Select(e => mapper.ToDto(e, stream.IsOwner)).ToList()));
    }

    private static async Task<Results<Ok<CarePlanDto>, ProblemHttpResult>> PlanAsync(
        long tankId, string? date, int? utcOffsetMinutes, ClaimsPrincipal user, TankAccessService access, CarePlanService plans, CancellationToken ct)
    {
        if (await StreamAccessAsync(tankId, user, access, ct) is not { } stream) return Problem("care_not_found");
        if (CarePlanService.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return Problem("care_date_invalid");
        return TypedResults.Ok(await plans.GetPlanAsync(tankId, today, offset, ct, includeOnlyMe: stream.IsOwner));
    }

    private static async Task<Results<Ok<CarePlanDto>, ProblemHttpResult>> ChangeRuleAsync(
        long tankId, string kind, UpdateCarePlanRuleRequest request, string? date, int? utcOffsetMinutes,
        ClaimsPrincipal user, CurrentProfileService profiles, CarePlanService plans, CancellationToken ct)
    {
        if (CarePlanService.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return Problem("care_date_invalid");
        var profile = await profiles.ResolveAsync(user, ct);
        var (plan, error) = await plans.ChangeRuleAsync(tankId, profile.Id, kind, request, today, offset, ct);
        return error is not null ? Problem(error) : TypedResults.Ok(plan!);
    }

    private static async Task<Results<Ok<CarePlanDto>, ProblemHttpResult>> ChangeRemindersAsync(
        long tankId, UpdateCarePlanRequest request, string? date, int? utcOffsetMinutes,
        ClaimsPrincipal user, CurrentProfileService profiles, CarePlanService plans, CancellationToken ct)
    {
        if (CarePlanService.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return Problem("care_date_invalid");
        var profile = await profiles.ResolveAsync(user, ct);
        var (plan, error) = await plans.ChangeRemindersAsync(tankId, profile.Id, request, today, offset, ct);
        return error is not null ? Problem(error) : TypedResults.Ok(plan!);
    }

    private static async Task<Results<Ok<DueCareDto>, ProblemHttpResult>> DueAsync(
        string? date, int? utcOffsetMinutes, ClaimsPrincipal user, CurrentProfileService profiles, CarePlanService plans, CancellationToken ct)
    {
        if (CarePlanService.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return Problem("care_date_invalid");
        var profile = await profiles.ResolveAsync(user, ct);
        return TypedResults.Ok(await plans.ListDueAsync(profile.Id, today, offset, ct));
    }

    private const int MaxSeriesPoints = 1000;

    private static async Task<Results<Ok<CareSeriesDto>, ProblemHttpResult>> SeriesAsync(
        long tankId, string? parameter, [FromQuery(Name = "from")] DateTimeOffset? start, [FromQuery(Name = "to")] DateTimeOffset? end,
        ClaimsPrincipal user, TankAccessService access, CichlidsDbContext db, CancellationToken ct)
    {
        if (await StreamAccessAsync(tankId, user, access, ct) is not { } stream) return Problem("care_not_found");
        if (!SnakeCaseEnum.TryParse<CareParameter>(parameter, out var kind)) return Problem("care_parameter_invalid");
        if (start > end) return Problem("care_range_invalid");
        var points = db.CareMeasurementValues.AsNoTracking().Where(v => v.Parameter == kind)
            .Join(db.CareEntries.Where(e => e.TankId == tankId && (stream.IsOwner || !e.OnlyMe) && (start == null || e.OccurredAt >= start) && (end == null || e.OccurredAt <= end)),
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

    internal static ProblemHttpResult Problem(string code) => ApiProblems.WithCode(
        code is "care_not_found" or "care_photo_not_found" or "todo_not_found" or "water_change_not_found" ? 404 : code == "care_photo_limit_reached" ? 409 : 400,
        "Care request refused", code switch
        {
            "care_not_found" => "This care entry or tank is unavailable.",
            "care_photo_not_found" => "This photo is unavailable or its link has expired.",
            "care_photo_invalid" => "Send one JPEG, PNG or WebP photo of up to 25 MB that can be decoded.",
            "care_photo_limit_reached" => "A care entry holds up to 6 photos.",
            "care_time_invalid" => "The time must not be more than two minutes in the future.",
            "care_time_fixed" => "The time of an entry from the guided water change is fixed; its note, amount and photos can be changed.",
            "care_kind_invalid" => "Choose water_change or measurement.",
            "care_amount_invalid" => "Enter a positive amount, up to 100 percent or 1000000 volume units.",
            "care_measurement_invalid" => "A measurement needs one to ten values with distinct parameters, and a water change takes none.",
            "care_parameter_invalid" => "Choose temperature, ph, kh, gh, no2, no3, nh4, po4, conductivity or tds.",
            "care_unit_invalid" => "This unit does not apply to the parameter.",
            "care_value_invalid" => "The value is outside the plausible range of the parameter.",
            "care_origin_invalid" => "Choose test_kit, meter or probe.",
            "water_change_not_found" => "This water change or tank is unavailable, or the water change is no longer open.",
            "water_change_request_invalid" => "Choose a known step with the state done or skipped.",
            "water_change_photo_limit" => "A water change holds up to 6 photos.",
            "todo_not_found" => "This to-do is unavailable.",
            "todo_text_invalid" => "A to-do has 1 to 200 characters.",
            "todo_request_invalid" => "Send done as true or false, or a new text, and list open, done or all to-dos.",
            "care_note_invalid" => "A note has up to 2000 characters.",
            "care_range_invalid" => "The start of the range must not be after its end.",
            "care_plan_invalid" => "Choose a rhythm of 1 to 90 days or switch the rule on or off, or send remindersEnabled as true or false; a measurement rule needs a rhythm.",
            "care_date_invalid" => "Send your local calendar date as YYYY-MM-DD and, optionally, the offset of your clock from UTC in minutes (-840 to 840).",
            _ => "Check the care request and its units.",
        }, code);
}
