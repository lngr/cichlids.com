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
    public List<CareMeasurementValue> Values { get; set; } = [];
}
