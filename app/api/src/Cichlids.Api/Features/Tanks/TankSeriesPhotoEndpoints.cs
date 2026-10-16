using System.Security.Claims;
using System.Text.RegularExpressions;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Uploads;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.Tanks;

public static partial class TankSeriesPhotoEndpoints
{
    /// <summary>
    /// Whether a request path is the route that adds a photo to a tank series, so framework
    /// refusals such as a missing login on this route carry a tank photo problem code.
    /// </summary>
    public static bool IsSeriesPhotoPath(PathString path) => path.HasValue && SeriesPhotoPath().IsMatch(path.Value!);

    [GeneratedRegex("^/api/tanks/[0-9]+/time-lapse/photos/?$", RegexOptions.CultureInvariant)]
    private static partial Regex SeriesPhotoPath();

    public static IEndpointRouteBuilder MapTankSeriesPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        // Callers authenticate with a bearer token, never a cookie, so antiforgery validation has nothing to protect.
        app.MapPost("/api/tanks/{tankId:long}/time-lapse/photos", AddAsync).WithName("AddTankSeriesPhoto").WithTags("Tanks")
            .RequireAuthorization().DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadsEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: UploadsEndpoints.MaxRequestBytes)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(429);
        return app;
    }

    private static async Task<Results<Created<TankTimeLapsePhotoDto>, ProblemHttpResult>> AddAsync(
        long tankId, IFormFile? file, [FromForm] bool? capturedInApp, ClaimsPrincipal user, CurrentProfileService profiles, TankSeriesPhotoService photos, CancellationToken ct)
    {
        var profile = await profiles.ResolveCallerAsync(user, ct);
        var (photo, error) = await photos.AddAsync(tankId, profile.Id, file, capturedInApp ?? false, ct);
        if (error == UploadsEndpoints.UploadRateLimitedCode) return UploadsEndpoints.RateLimitProblem();
        return error is not null ? Problem(error) : TypedResults.Created($"/api/tanks/{tankId}/time-lapse", photo!);
    }

    private static ProblemHttpResult Problem(string code) => code == TankSeriesPhotoService.NotFoundCode
        ? ApiProblems.WithCode(StatusCodes.Status404NotFound, "Tank unavailable", "This tank is unavailable.", code)
        : ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Photo refused",
            "Send one JPEG, PNG or WebP photo of up to 25 MB that can be decoded.", code);
}
