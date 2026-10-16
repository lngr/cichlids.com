using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// Serves the media of tanks whose files live under the private storage prefix. The token in the path
/// is the credential: it is issued only to a viewer who sees the tank, names one photo variant or one
/// video and expires. A token resolves only while the media item has a link to a tank, so removed
/// media stop resolving.
/// </summary>
public static class TankMediaEndpoints
{
    public const string NotFoundCode = "tank_media_not_found";

    public static IEndpointRouteBuilder MapTankMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tank-media/{token}", ServePhotoAsync).WithName("GetTankMedia").WithTags("Tanks").AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg").ProducesProblem(StatusCodes.Status404NotFound);
        app.MapGet("/api/tank-videos/{token}/{**path}", ServeVideoAsync).WithName("GetTankVideoFile").WithTags("Tanks").AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.apple.mpegurl").ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ServePhotoAsync(
        string token, TankMediaUrlSigner signer, CichlidsDbContext db, IObjectStore store, HttpContext http, CancellationToken ct)
    {
        if (signer.TryReadPhoto(token) is not { } variantId) return NotFound();
        var key = await db.MediaVariants.AsNoTracking()
            .Where(v => v.Id == variantId && db.MediaLinks.Any(l => l.MediaItemId == v.MediaItemId && l.TankId != null && l.Role != MediaLinkRole.Video))
            .Select(v => v.StorageKey).SingleOrDefaultAsync(ct);
        var stream = key is null ? null : await store.GetAsync(key, ct);
        if (stream is null) return NotFound();
        http.Response.Headers.CacheControl = "private, max-age=300";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.Stream(stream, MediaStorageKeys.ResolveContentType(key!));
    }

    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ServeVideoAsync(
        string token, string path, TankMediaUrlSigner signer, CichlidsDbContext db, IObjectStore store, HttpContext http, CancellationToken ct)
    {
        if (signer.TryReadVideo(token) is not { } mediaItemId || !MediaStorageKeys.IsHlsFile(path)) return NotFound();
        var master = await db.MediaVariants.AsNoTracking()
            .Where(v => v.MediaItemId == mediaItemId && v.Label == VideoStorage.HlsLabel
                && db.MediaLinks.Any(l => l.MediaItemId == mediaItemId && l.TankId != null && l.Role == MediaLinkRole.Video))
            .Select(v => v.StorageKey).SingleOrDefaultAsync(ct);
        var stream = master is null ? null : await store.GetAsync(master[..(master.LastIndexOf('/') + 1)] + path, ct);
        if (stream is null) return NotFound();
        http.Response.Headers.CacheControl = "private, max-age=300";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.Stream(stream, MediaStorageKeys.ResolveHlsContentType(path));
    }

    private static ProblemHttpResult NotFound() =>
        ApiProblems.WithCode(StatusCodes.Status404NotFound, "Media unavailable", "This media item is unavailable or its link has expired.", NotFoundCode);
}
