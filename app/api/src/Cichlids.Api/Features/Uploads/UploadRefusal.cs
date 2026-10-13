namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// The upload limit a member has reached: the number of open drafts or the number of uploads
/// within the last hour.
/// </summary>
public enum UploadRefusal
{
    DraftLimitReached,
    RateLimited,
}
