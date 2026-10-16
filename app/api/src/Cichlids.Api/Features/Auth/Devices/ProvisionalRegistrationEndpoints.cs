using System.Security.Claims;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Me;
using Cichlids.Api.Features.Profiles;
using Cichlids.Api.Features.Streaks;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// The routes through which an app installation hands its provisional profile to the Keycloak account
/// of the member: a preview of what registering does with the content, the upgrade of the
/// provisional profile into the profile of a new account and the move of the content into the
/// profile of an existing account. Every route needs the Keycloak token of the account as bearer and
/// a signed challenge of the device in the body, and refuses either alone. Preview and move take the local
/// date of the caller (date, utcOffsetMinutes) like the vacation routes, since moved vacations are judged
/// against the repair window. A refused proof answers
/// 403 with the code device_auth_refused, since the Keycloak token of the call is valid.
/// </summary>
public static class ProvisionalRegistrationEndpoints
{
    public const string AccountHasProfileCode = "account_has_profile";
    public const string AccountHasNoProfileCode = "account_has_no_profile";
    public const string AccountSuspendedCode = "account_suspended";

    public static IEndpointRouteBuilder MapProvisionalRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/provisional").WithTags("ProvisionalRegistration").RequireRegistered();

        group.MapPost("/preview", PreviewAsync).WithName("PreviewProvisionalRegistration")
            .RequireRateLimiting(DeviceAuthEndpoints.TokenLimiter)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPost("/upgrade", UpgradeAsync).WithName("UpgradeProvisionalProfile")
            .RequireRateLimiting(DeviceAuthEndpoints.TokenLimiter)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPost("/move", MoveAsync).WithName("MoveProvisionalContent")
            .RequireRateLimiting(DeviceAuthEndpoints.TokenLimiter)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<Results<Ok<ProvisionalPreviewDto>, ProblemHttpResult>> PreviewAsync(
        DeviceTokenRequest? proof, string? date, int? utcOffsetMinutes, ClaimsPrincipal user, ProvisionalRegistrationService service,
        StreakService streaks, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        if (streaks.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return StreakEndpoints.Problem(StreakEndpoints.DateInvalidCode);
        var result = proof is null ? Refused<ProvisionalPreviewDto>() : await service.PreviewAsync(user, proof, today, offset, cancellationToken);
        return result.Failure is { } failure ? Problem(failure) : TypedResults.Ok(result.Value!);
    }

    private static async Task<Results<Ok<MeDto>, ProblemHttpResult>> UpgradeAsync(
        DeviceTokenRequest? proof, ClaimsPrincipal user, ProvisionalRegistrationService service,
        ProfilesQueryService profilesQuery, CichlidsDbContext context, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        var result = proof is null ? Refused<long>() : await service.UpgradeAsync(user, proof, cancellationToken);
        return result.Failure is { } failure
            ? Problem(failure)
            : TypedResults.Ok(await MeAsync(result.Value, user, profilesQuery, context, cancellationToken));
    }

    private static async Task<Results<Ok<ProvisionalMoveDto>, ProblemHttpResult>> MoveAsync(
        DeviceTokenRequest? proof, string? date, int? utcOffsetMinutes, ClaimsPrincipal user, ProvisionalRegistrationService service,
        StreakService streaks, ProfilesQueryService profilesQuery, CichlidsDbContext context, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        if (streaks.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return StreakEndpoints.Problem(StreakEndpoints.DateInvalidCode);
        var result = proof is null ? Refused<MoveSummary>() : await service.MoveAsync(user, proof, today, offset, cancellationToken);
        if (result.Failure is { } failure)
        {
            return Problem(failure);
        }

        var summary = result.Value!;
        return TypedResults.Ok(new ProvisionalMoveDto(
            summary.Moved, summary.VacationsDropped, summary.VacationsDroppedRepair, await MeAsync(summary.ProfileId, user, profilesQuery, context, cancellationToken)));
    }

    private static ProvisionalRegistrationResult<T> Refused<T>() =>
        ProvisionalRegistrationResult<T>.Fail(ProvisionalRegistrationFailure.ProofRefused);

    private static async Task<MeDto> MeAsync(
        long profileId, ClaimsPrincipal user, ProfilesQueryService profilesQuery, CichlidsDbContext context,
        CancellationToken cancellationToken)
    {
        var profile = await context.Profiles.AsNoTracking().SingleAsync(p => p.Id == profileId, cancellationToken);
        var detail = await profilesQuery.GetOwnAsync(profile, cancellationToken)
            ?? throw new InvalidOperationException($"The profile {profileId} has no public detail view.");
        var roles = user.Identity is ClaimsIdentity identity
            ? identity.FindAll(identity.RoleClaimType).Select(c => c.Value).Distinct().ToList()
            : [];
        return new MeDto(detail, roles);
    }

    private static ProblemHttpResult Problem(ProvisionalRegistrationFailure failure) => failure switch
    {
        ProvisionalRegistrationFailure.AccountHasProfile => ApiProblems.WithCode(
            StatusCodes.Status409Conflict, "Account has a profile",
            "This account already has a profile. Move the content of the installation into it instead.", AccountHasProfileCode),
        ProvisionalRegistrationFailure.AccountHasNoProfile => ApiProblems.WithCode(
            StatusCodes.Status409Conflict, "Account has no profile",
            "This account has no profile yet. Upgrade the profile of the installation to it instead.", AccountHasNoProfileCode),
        ProvisionalRegistrationFailure.AccountSuspended => ApiProblems.WithCode(
            StatusCodes.Status403Forbidden, "Account suspended", "This account is suspended and cannot receive content.", AccountSuspendedCode),
        _ => ApiProblems.WithCode(
            StatusCodes.Status403Forbidden, "Device proof refused",
            "The request needs the signed challenge of an active device credential of a provisional profile.", DeviceAuthEndpoints.RefusedCode),
    };
}
