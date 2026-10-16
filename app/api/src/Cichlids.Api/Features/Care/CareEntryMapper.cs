using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Builds the care entry representation of the API from an entry loaded with CareEntryMapper.WithDetails,
/// with the signed URLs of its photos.
/// </summary>
public sealed class CareEntryMapper(CarePhotoUrlSigner signer)
{
    /// <summary>
    /// Loads everything an entry representation needs: the measurement values, the to-dos and the
    /// photos with the variants of their media items.
    /// </summary>
    public static IQueryable<CareEntry> WithDetails(IQueryable<CareEntry> entries) =>
        entries.Include(e => e.Values).Include(e => e.Todos).Include(e => e.Photos).ThenInclude(p => p.MediaItem).ThenInclude(m => m.Variants).AsSplitQuery();

    public CareEntryDto ToDto(CareEntry entry) => new(entry.Id, entry.TankId, entry.AuthorProfileId,
        entry.Kind, entry.OccurredAt, entry.RecordedAt, entry.AmountInputValue is { } value
            ? new(value, entry.AmountInputUnit!.Value, entry.AmountLiters) : null,
        entry.Kind == CareKind.Measurement
            ? entry.Values.OrderBy(v => v.Parameter).Select(MeasurementValueDto.From).ToList() : null,
        entry.Note,
        entry.Photos.OrderBy(p => p.Position).Select(ToDto).ToList(),
        entry.Todos.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(CareTodoDto.From).ToList());

    private CarePhotoDto ToDto(CarePhoto photo)
    {
        string? UrlOf(string label) => photo.MediaItem.Variants.SingleOrDefault(v => v.Label == label) is { } variant ? signer.PathFor(variant.Id) : null;
        return new(photo.Id, photo.MediaItem.Width, photo.MediaItem.Height,
            new CarePhotoUrlsDto(UrlOf("thumb"), UrlOf("small"), UrlOf("medium"), UrlOf("large")));
    }
}
