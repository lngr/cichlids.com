using Cichlids.Api.Features.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// A refusal of a video request: the HTTP status, the stable problem code, a message for
/// developers and optional extra members of the problem details, such as the current offset of an
/// upload.
/// </summary>
public sealed record VideoProblem(int Status, string Code, string Detail, IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public const string UploadNotFound = "video_upload_not_found";
    public const string UploadInvalid = "video_upload_invalid";
    public const string OffsetMismatch = "video_upload_offset_mismatch";
    public const string UploadComplete = "video_upload_complete";
    public const string UploadIncomplete = "video_upload_incomplete";
    public const string UploadExpired = "video_upload_expired";
    public const string UploadLimitReached = "video_upload_limit_reached";
    public const string ChunkTooLarge = "video_chunk_too_large";
    public const string ChunkIncomplete = "video_chunk_incomplete";
    public const string FormatUnsupported = "video_format_unsupported";
    public const string TooLarge = "video_too_large";
    public const string TargetNotFound = "video_target_not_found";
    public const string NotFound = "video_not_found";
    public const string CareVideoLimitReached = "care_video_limit_reached";
    public const string RequestInvalid = "video_request_invalid";
    public const string AuthRequired = "video_auth_required";

    public ProblemHttpResult ToResult()
    {
        var extensions = new Dictionary<string, object?> { ["code"] = Code };
        if (Extensions is not null)
        {
            foreach (var (name, value) in Extensions)
            {
                extensions[name] = value;
            }
        }

        return TypedResults.Problem(title: "Video request refused", detail: Detail, statusCode: Status, extensions: extensions);
    }

    public static VideoProblem OfProbe(string code) => new(StatusCodes.Status422UnprocessableEntity, code, "The video was refused.");
}
