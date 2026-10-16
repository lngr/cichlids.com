namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// Limits on what one member may upload, bound from the "UploadLimits" configuration section. Provisional profiles have lower limits of their own.
/// </summary>
public sealed class UploadLimitsOptions
{
    public const string SectionName = "UploadLimits";

    /// <summary>
    /// Number of photos one member may upload within the last hour, counted from the stored
    /// uploads.
    /// </summary>
    public int MaxUploadsPerHour { get; set; } = 30;

    /// <summary>
    /// Number of photos and videos the provisional profile of an app installation may upload within
    /// the last hour, counted from the stored uploads.
    /// </summary>
    public int ProvisionalMaxUploadsPerHour { get; set; } = 10;

    /// <summary>
    /// Number of unpublished drafts one member may hold at the same time.
    /// </summary>
    public int MaxOpenDrafts { get; set; } = 50;
}
