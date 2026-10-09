namespace Cichlids.Api.Features.Media;

/// <summary>
/// Public URLs for one media item's renditions, keyed by their fixed labels. A variant is null
/// when the media pipeline has not derived it yet (for example while the media backfill for a
/// legacy item is still running); <see cref="Original"/> always resolves since it is the
/// uploaded file itself.
/// </summary>
public sealed record ImageUrlsDto(string? Thumb, string? Small, string? Medium, string? Large, string Original);
