using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

public class CareEntry
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public long AuthorProfileId { get; set; }
    public CareKind Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public decimal? AmountInputValue { get; set; }
    public CareAmountUnit? AmountInputUnit { get; set; }
    public decimal? AmountLiters { get; set; }

    /// <summary>
    /// A free text of up to 2000 characters the author attached to the entry; null when there is none.
    /// </summary>
    public string? Note { get; set; }

    public List<CareMeasurementValue> Values { get; set; } = [];
    public List<CarePhoto> Photos { get; set; } = [];
    public List<CareVideo> Videos { get; set; } = [];
    public List<CareTodo> Todos { get; set; } = [];
}
