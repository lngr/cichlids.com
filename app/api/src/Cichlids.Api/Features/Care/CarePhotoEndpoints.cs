using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Uploads;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

public static class CarePhotoEndpoints
{
    public static IEndpointRouteBuilder MapCarePhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var entry = app.MapGroup("/api/tanks/{tankId:long}/care/{id:long}/photos").WithTags("Care");
        // Callers authenticate with a bearer token, never a cookie, so antiforgery validation has nothing to protect.
        entry.MapPost("", AddAsync).WithName("AddCarePhoto").RequireAuthorization().DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadsEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: UploadsEndpoints.MaxRequestBytes)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(429);
        entry.MapDelete("/{photoId:long}", RemoveAsync).WithName("RemoveCarePhoto").RequireAuthorization()
            .ProducesProblem(401).ProducesProblem(404);
        app.MapGet("/api/care-photos/{token}", ServeAsync).WithName("GetCarePhoto").WithTags("Care").AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg").ProducesProblem(404);
        return app;
    }

    private static async Task<Results<Created<CareEntryDto>, ProblemHttpResult>> AddAsync(
        long tankId, long id, IFormFile? file, ClaimsPrincipal user, CurrentProfileService profiles, CarePhotoService photos, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (entry, error) = await photos.AddAsync(tankId, id, profile.Id, file, ct);
        if (error == "upload_rate_limited") return UploadsEndpoints.RateLimitProblem();
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.Created($"/api/tanks/{tankId}/care/{id}", entry!);
    }

    private static async Task<Results<Ok<CareEntryDto>, ProblemHttpResult>> RemoveAsync(
        long tankId, long id, long photoId, ClaimsPrincipal user, CurrentProfileService profiles, CarePhotoService photos, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (entry, error) = await photos.RemoveAsync(tankId, id, photoId, profile.Id, ct);
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.Ok(entry!);
    }

    // The token is the credential: it is issued only to the owner of the entry, names one variant and
    // expires. The variant must still belong to a care photo or a photo of a water change session, so a removed photo stops resolving.
    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ServeAsync(
        string token, CarePhotoUrlSigner signer, CichlidsDbContext db, IObjectStore store, HttpContext http, CancellationToken ct)
    {
        if (signer.TryRead(token) is not { } variantId) return CareEndpoints.Problem("care_photo_not_found");
        var key = await db.MediaVariants.AsNoTracking()
            .Where(v => v.Id == variantId && (db.CarePhotos.Any(p => p.MediaItemId == v.MediaItemId)
                || db.WaterChangeSessionPhotos.Any(p => p.MediaItemId == v.MediaItemId)))
            .Select(v => v.StorageKey).SingleOrDefaultAsync(ct);
        var stream = key is null ? null : await store.GetAsync(key, ct);
        if (stream is null) return CareEndpoints.Problem("care_photo_not_found");
        http.Response.Headers.CacheControl = "private, max-age=300";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.Stream(stream, "image/jpeg");
    }
}
