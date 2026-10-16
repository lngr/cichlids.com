using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Streaks;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.Library;

/// <summary>
/// The media library of the signed-in member: the files the member owns with or without links. Every route
/// answers for the caller's own files only; a file of another member answers like a missing file.
/// </summary>
public static class MediaLibraryEndpoints
{
    public const string FilterInvalidCode = "library_filter_invalid";

    public static IEndpointRouteBuilder MapMediaLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/media", ListAsync).WithName("ListMyMedia").WithTags("Me").RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        app.MapGet("/api/me/media/{id:long}", GetAsync).WithName("GetMyMedia").WithTags("Me").RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        app.MapDelete("/api/me/media/{id:long}", DeleteAsync).WithName("DeleteMyMedia").WithTags("Me").RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        // Callers authenticate with a bearer token, never a cookie, so antiforgery validation has nothing to protect.
        app.MapPost("/api/me/media/photos", UploadPhotoAsync).WithName("UploadMyMediaPhoto").WithTags("Me").RequireAuthorization()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadsEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: UploadsEndpoints.MaxRequestBytes)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<Results<Ok<PagedResponse<MediaLibraryItemDto>>, ProblemHttpResult>> ListAsync(
        string? kind, string? filter, int? offset, int? limit, ClaimsPrincipal user, CurrentProfileService profiles,
        MediaLibraryQueryService library, CancellationToken cancellationToken)
    {
        MediaKind? wantedKind = null;
        if (kind is not null)
        {
            if (!SnakeCaseEnum.TryParse<MediaKind>(kind, out var parsed)) return FilterProblem();
            wantedKind = parsed;
        }

        bool? linked = filter switch
        {
            null => null,
            "linked" => true,
            "unlinked" => false,
            _ => null,
        };
        if (filter is not null && linked is null) return FilterProblem();

        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
        var (skip, take) = Pagination.Normalize(offset, limit);
        return TypedResults.Ok(await library.ListAsync(profile.Id, wantedKind, linked, skip, take, cancellationToken));
    }

    private static async Task<Results<Ok<MediaLibraryDetailDto>, ProblemHttpResult>> GetAsync(
        long id, string? date, int? utcOffsetMinutes, ClaimsPrincipal user, CurrentProfileService profiles, MediaLibraryQueryService library,
        StreakService streaks, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var offset = 0;
        if (date is not null || utcOffsetMinutes is not null)
        {
            if (streaks.ParseLocalTime(date ?? today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), utcOffsetMinutes) is not var (local, localOffset))
                return ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Invalid date",
                    "Send your local calendar date as YYYY-MM-DD and, optionally, the offset of your clock from UTC in minutes.", StreakEndpoints.DateInvalidCode);
            (today, offset) = (local, localOffset);
        }

        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
        var detail = await library.GetAsync(profile.Id, id, today, offset, cancellationToken);
        return detail is null ? NotFound() : TypedResults.Ok(detail);
    }

    private static async Task<Results<Ok<MediaLibraryDeletedDto>, ProblemHttpResult>> DeleteAsync(
        long id, ClaimsPrincipal user, CurrentProfileService profiles, MediaLibraryService library, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
        var (places, error) = await library.DeleteAsync(profile.Id, id, cancellationToken);
        return error switch
        {
            null => TypedResults.Ok(new MediaLibraryDeletedDto(places!)),
            MediaLibraryService.IsProofCode => ApiProblems.WithCode(StatusCodes.Status409Conflict, "File proves a decommissioning",
                "This photo proves the decommissioning of a tank and stays in your library.", error),
            _ => NotFound(),
        };
    }

    private static async Task<Results<Created<MediaLibraryItemDto>, ProblemHttpResult>> UploadPhotoAsync(
        IFormFile? file, ClaimsPrincipal user, CurrentProfileService profiles, MediaLibraryService library, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
        var (item, error) = await library.AddPhotoAsync(profile.Id, file, cancellationToken);
        return error switch
        {
            null => TypedResults.Created($"/api/me/media/{item!.Id}", item),
            UploadsEndpoints.UploadRateLimitedCode => UploadsEndpoints.RateLimitProblem(),
            _ => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Photo refused",
                "Send one JPEG, PNG or WebP photo of up to 25 MB that can be decoded.", error),
        };
    }

    private static ProblemHttpResult FilterProblem() => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Invalid filter",
        "Send kind as photo or video and filter as linked or unlinked.", FilterInvalidCode);

    private static ProblemHttpResult NotFound() => ApiProblems.WithCode(StatusCodes.Status404NotFound, "File unavailable",
        "This file does not exist in your media library.", MediaLibraryService.NotFoundCode);
}
