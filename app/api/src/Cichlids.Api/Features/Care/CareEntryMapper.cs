using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Builds the care entry representation of the API from an entry loaded with CareEntryMapper.WithDetails,
/// with the signed URLs of its photos and videos.
/// </summary>
public sealed class CareEntryMapper(MediaUrlSigner signer, CichlidsDbContext context, TimeProvider clock)
{
    // The visibility generation of each media item the prepared viewer sees, read together with the check.
    // An item that is not in it gets no signed path.
    private readonly Dictionary<long, long> _issuable = [];

    /// <summary>
    /// Reads, for the media items of the given entries, which ones the viewer sees and their visibility
    /// generation in one statement. The representations built afterwards carry signed paths for these items
    /// only. A null viewer is anonymous.
    /// </summary>
    public Task PrepareAsync(IEnumerable<CareEntry> entries, long? viewerProfileId, CancellationToken cancellationToken) =>
        PrepareAsync(entries.SelectMany(e => e.MediaLinks).Select(l => l.MediaItemId), viewerProfileId, cancellationToken);

    /// <summary>
    /// Reads the visibility generation of the given media items that the viewer sees, as the entries overload does.
    /// </summary>
    public async Task PrepareAsync(IEnumerable<long> mediaItemIds, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var ids = mediaItemIds.Distinct().ToList();
        foreach (var (id, generation) in await context.VisibleMediaGenerationsAsync(ids, viewerProfileId, clock.GetUtcNow(), cancellationToken))
        {
            _issuable[id] = generation;
        }
    }

    /// <summary>
    /// Loads everything an entry representation needs: the measurement values, the to-dos and the
    /// photos and videos with the variants of their media items.
    /// </summary>
    public static IQueryable<CareEntry> WithDetails(IQueryable<CareEntry> entries) =>
        entries.Include(e => e.Values).Include(e => e.Todos).Include(e => e.MediaLinks).ThenInclude(l => l.MediaItem).ThenInclude(m => m.Variants)
            .Include(e => e.MediaLinks).ThenInclude(l => l.MediaItem).ThenInclude(m => m.VideoJob).AsSplitQuery();

    /// <summary>
    /// The representation of an entry for its owner, or, when the reader is not the owner, without the
    /// to-dos the owner attached to it.
    /// </summary>
    public CareEntryDto ToDto(CareEntry entry, bool forOwner) =>
        forOwner ? ToDto(entry) : ToDto(entry) with { Todos = [] };

    public CareEntryDto ToDto(CareEntry entry) => new(entry.Id, entry.TankId, entry.AuthorProfileId,
        entry.Kind, entry.OccurredAt, entry.RecordedAt, entry.AmountInputValue is { } value
            ? new(value, entry.AmountInputUnit!.Value, entry.AmountLiters) : null,
        entry.Kind == CareKind.Measurement
            ? entry.Values.OrderBy(v => v.Parameter).Select(MeasurementValueDto.From).ToList() : null,
        entry.Note,
        entry.MediaLinks.Where(l => l.Role == MediaLinkRole.Photo).OrderBy(l => l.Position).Select(ToDto).ToList(),
        entry.MediaLinks.Where(l => l.Role == MediaLinkRole.Video).OrderBy(l => l.Position).Select(l => VideoQueryService.ToSignedDto(l.MediaItem, signer, _issuable.TryGetValue(l.MediaItemId, out var generation) ? generation : null)).ToList(),
        entry.Todos.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(CareTodoDto.From).ToList(),
        entry.DurationSeconds, entry.WaterChangeSessionId, EvidenceOf(entry), entry.OnlyMe);

    // A water change logged without the guided mode has the evidence state none; a measurement has no evidence.
    private static WaterChangeEvidenceDto? EvidenceOf(CareEntry entry) => entry.Kind != CareKind.WaterChange ? null
        : entry.Evidence is { } state ? new(state, entry.EvidenceMissing) : WaterChangeEvidenceDto.From(WaterChangeEvidence.NoGuidedMode);

    private CarePhotoDto ToDto(MediaLink link) => new(link.Id, link.MediaItem.Width, link.MediaItem.Height, UrlsOf(link.MediaItem));

    /// <summary>
    /// The signed URLs of the variants of a photo media item whose variants are loaded.
    /// </summary>
    public CarePhotoUrlsDto UrlsOf(MediaItem item)
    {
        string? UrlOf(string label) => item.Variants.SingleOrDefault(v => v.Label == label) is { } variant && _issuable.TryGetValue(item.Id, out var generation) ? signer.PhotoPathFor(variant.Id, generation) : null;
        return new(UrlOf("thumb"), UrlOf("small"), UrlOf("medium"), UrlOf("large"));
    }
}
