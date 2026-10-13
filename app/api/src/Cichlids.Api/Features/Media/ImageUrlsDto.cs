namespace Cichlids.Api.Features.Media;

/// <summary>
/// Public URLs for one media item's variants, keyed by their fixed labels. A variant is null when
/// the media pipeline has not derived it, for example while the media backfill for a legacy item
/// is running or when the original is narrower than that variant's target width. Originals are
/// private and have no public URL.
/// </summary>
public sealed record ImageUrlsDto(string? Thumb, string? Small, string? Medium, string? Large);
