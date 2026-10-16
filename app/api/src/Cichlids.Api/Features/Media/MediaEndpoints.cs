using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Media;

/// <summary>
/// Serves the private photos and videos of every context. The token in the path is the credential: the
/// API issues it only to a viewer who sees the media item. The route checks the signature, the expiry and
/// that the visibility generation in the token equals the current one of the media item, so a link ends
/// with the next change that can alter who sees the item. A token names one photo variant or one video;
/// every request, including the segments under a playlist token, makes the same check, and a deleted media
/// item or variant does not resolve.
/// </summary>
public static class MediaEndpoints
{
    public const string NotFoundCode = "media_not_found";

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/media/photos/{token}", ServePhotoAsync).WithName("GetMediaPhoto").WithTags("Media").AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg").ProducesProblem(StatusCodes.Status404NotFound);
        app.MapGet("/api/media/videos/{token}/{**path}", ServeVideoAsync).WithName("GetMediaVideoFile").WithTags("Media").AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.apple.mpegurl").ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ServePhotoAsync(
        string token, MediaUrlSigner signer, CichlidsDbContext db, IObjectStore store, HttpContext http, CancellationToken ct)
    {
        if (signer.TryReadPhoto(token) is not var (variantId, generation)) return NotFound();
        var key = await db.MediaVariants.AsNoTracking()
            .Where(v => v.Id == variantId && db.MediaItems.Any(m => m.Id == v.MediaItemId && m.Kind == MediaKind.Photo
                && m.VisibilityGeneration == generation))
            .Select(v => v.StorageKey).SingleOrDefaultAsync(ct);
        var stream = key is null ? null : await store.GetAsync(key, ct);
        if (stream is null) return NotFound();
        http.Response.Headers.CacheControl = "private, max-age=300";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.Stream(stream, MediaStorageKeys.ResolveContentType(key!));
    }

    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ServeVideoAsync(
        string token, string path, MediaUrlSigner signer, CichlidsDbContext db, IObjectStore store, HttpContext http, CancellationToken ct)
    {
        if (signer.TryReadVideo(token) is not var (mediaItemId, generation) || !MediaStorageKeys.IsHlsFile(path)) return NotFound();
        var master = await db.MediaVariants.AsNoTracking()
            .Where(v => v.MediaItemId == mediaItemId && v.Label == VideoStorage.HlsLabel
                && db.MediaItems.Any(m => m.Id == mediaItemId && m.VisibilityGeneration == generation))
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
