using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Common;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Videos;

public static partial class VideoEndpoints
{
    public const string OffsetHeader = "Upload-Offset";
    public const string ChunkContentType = "application/offset+octet-stream";
    public const string CareVideoNotFoundCode = "care_video_not_found";

    /// <summary>
    /// Whether a request path is one of the exact routes of the video upload and of the videos of a
    /// tank, so framework refusals such as a missing login on these routes carry a video problem code.
    /// </summary>
    public static bool IsVideoPath(PathString path) => path.HasValue && VideoPath().IsMatch(path.Value!);

    [GeneratedRegex("^(/api/video-uploads(/[0-9]+(/complete)?)?|/api/tanks/[0-9]+/videos(/[0-9]+)?)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoPath();

    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder app)
    {
        var uploads = app.MapGroup("/api/video-uploads").WithTags("Videos");
        uploads.MapPost("", CreateAsync).WithName("CreateVideoUpload").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(413).ProducesProblem(429);
        uploads.MapGet("/{id:long}", GetAsync).WithName("GetVideoUpload").RequireAuthorization()
            .ProducesProblem(401).ProducesProblem(404);
        // Callers authenticate with a bearer token or the web session cookie of the same-origin web app.
        uploads.MapPatch("/{id:long}", AppendAsync).WithName("AppendVideoUploadChunk").RequireAuthorization().DisableAntiforgery()
            .Accepts<byte[]>(ChunkContentType)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(410).ProducesProblem(413);
        uploads.MapPost("/{id:long}/complete", CompleteAsync).WithName("CompleteVideoUpload").RequireAuthorization()
            .ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);
        uploads.MapDelete("/{id:long}", CancelAsync).WithName("CancelVideoUpload").RequireAuthorization()
            .ProducesProblem(401).ProducesProblem(404);

        var tank = app.MapGroup("/api/tanks/{tankId:long}/videos").WithTags("Videos");
        tank.MapGet("", ListAsync).WithName("ListTankVideos").AllowAnonymous().ProducesProblem(404);
        tank.MapDelete("/{mediaItemId:long}", RemoveFromTankAsync).WithName("RemoveTankVideo").RequireAuthorization()
            .ProducesProblem(401).ProducesProblem(404);

        app.MapDelete("/api/tanks/{tankId:long}/care/{id:long}/videos/{mediaItemId:long}", RemoveFromCareEntryAsync)
            .WithName("RemoveCareVideo").WithTags("Care").RequireAuthorization().ProducesProblem(401).ProducesProblem(404);
        app.MapGet("/api/care-videos/{token}/{**path}", ServeCareVideoAsync).WithName("GetCareVideoFile").WithTags("Care").AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.apple.mpegurl").ProducesProblem(404);
        return app;
    }

    private static async Task<Results<Created<VideoUploadDto>, ProblemHttpResult>> CreateAsync(
        CreateVideoUploadRequest request, ClaimsPrincipal user, CurrentProfileService profiles, VideoUploadService uploads, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (session, problem) = await uploads.CreateAsync(profile.Id, request, ct);
        return problem is not null ? problem.ToResult() : TypedResults.Created($"/api/video-uploads/{session!.Id}", session);
    }

    private static async Task<Results<Ok<VideoUploadDto>, ProblemHttpResult>> GetAsync(
        long id, ClaimsPrincipal user, CurrentProfileService profiles, VideoUploadService uploads, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (session, problem) = await uploads.GetAsync(profile.Id, id, ct);
        return problem is not null ? problem.ToResult() : TypedResults.Ok(session!);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> AppendAsync(
        long id, HttpContext http, ClaimsPrincipal user, CurrentProfileService profiles, VideoUploadService uploads, CancellationToken ct)
    {
        // The service reads at most one chunk plus one byte, so the host's body size limit is lifted.
        var feature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = null;
        }

        var profile = await profiles.ResolveAsync(user, ct);
        long? offset = long.TryParse(http.Request.Headers[OffsetHeader], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        var (next, problem) = await uploads.AppendAsync(profile.Id, id, offset, http.Request.ContentLength, http.Request.Body, ct);
        if (problem is not null)
        {
            return problem.ToResult();
        }

        http.Response.Headers[OffsetHeader] = next!.Value.ToString(CultureInfo.InvariantCulture);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<VideoDto>, ProblemHttpResult>> CompleteAsync(
        long id, ClaimsPrincipal user, CurrentProfileService profiles, VideoUploadService uploads, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (video, problem) = await uploads.CompleteAsync(profile.Id, id, ct);
        return problem is not null ? problem.ToResult() : TypedResults.Created($"/api/tanks/videos/{video!.Id}", video);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> CancelAsync(
        long id, ClaimsPrincipal user, CurrentProfileService profiles, VideoUploadService uploads, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        return await uploads.CancelAsync(profile.Id, id, ct) is { } problem ? problem.ToResult() : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<VideoDto>>, ProblemHttpResult>> ListAsync(
        long tankId, ClaimsPrincipal user, CichlidsDbContext db, VideoQueryService videos, CancellationToken ct)
    {
        var subject = user.FindFirst("sub")?.Value;
        long? viewer = user.Identity?.IsAuthenticated == true && subject is not null
            ? await db.ProfileIdentities.AsNoTracking().Where(i => i.Provider == "oidc" && i.Subject == subject)
                .Select(i => (long?)i.ProfileId).FirstOrDefaultAsync(ct)
            : null;
        var list = await videos.ListForTankAsync(tankId, viewer, ct);
        return list is null
            ? ApiProblems.WithCode(StatusCodes.Status404NotFound, "Tank unavailable", "This tank is unavailable.", "tank_not_found")
            : TypedResults.Ok(list);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveFromTankAsync(
        long tankId, long mediaItemId, ClaimsPrincipal user, CurrentProfileService profiles, VideoRemovalService removal, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        return await removal.RemoveFromTankAsync(tankId, mediaItemId, profile.Id, ct)
            ? TypedResults.NoContent()
            : new VideoProblem(StatusCodes.Status404NotFound, VideoProblem.NotFound, "The video is unavailable.").ToResult();
    }

    private static async Task<Results<Ok<CareEntryDto>, ProblemHttpResult>> RemoveFromCareEntryAsync(
        long tankId, long id, long mediaItemId, ClaimsPrincipal user, CurrentProfileService profiles, VideoRemovalService removal, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        return await removal.RemoveFromCareEntryAsync(tankId, id, mediaItemId, profile.Id, ct) is { } entry
            ? TypedResults.Ok(entry)
            : CareEndpoints.Problem("care_not_found");
    }

    // The token is the credential: it is issued only to the owner of the care entry, names one video
    // and expires. The path must be one of the files a transcode writes, and the video must still be
    // attached to a care entry, so a removed video stops resolving.
    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ServeCareVideoAsync(
        string token, string path, CareVideoUrlSigner signer, CichlidsDbContext db, IObjectStore store, HttpContext http, CancellationToken ct)
    {
        if (signer.TryRead(token) is not { } mediaItemId || !MediaStorageKeys.IsHlsFile(path))
        {
            return NotFound();
        }

        var master = await db.MediaVariants.AsNoTracking()
            .Where(v => v.MediaItemId == mediaItemId && v.Label == VideoStorage.HlsLabel && db.CareVideos.Any(c => c.MediaItemId == mediaItemId))
            .Select(v => v.StorageKey).SingleOrDefaultAsync(ct);
        var stream = master is null ? null : await store.GetAsync(master[..(master.LastIndexOf('/') + 1)] + path, ct);
        if (stream is null)
        {
            return NotFound();
        }

        http.Response.Headers.CacheControl = "private, max-age=300";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.Stream(stream, MediaStorageKeys.ResolveHlsContentType(path));
    }

    private static ProblemHttpResult NotFound() =>
        ApiProblems.WithCode(StatusCodes.Status404NotFound, "Video unavailable", "This video is unavailable.", CareVideoNotFoundCode);
}
