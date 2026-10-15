using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

public class CareMeasurementValue
{
    public long Id { get; set; }
    public long CareEntryId { get; set; }
    public CareParameter Parameter { get; set; }
    public decimal InputValue { get; set; }
    public MeasurementUnit InputUnit { get; set; }
    public decimal NormalizedValue { get; set; }
    public MeasurementOrigin? Origin { get; set; }
}
