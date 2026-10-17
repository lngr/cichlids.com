using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// An event on the timeline of a named fish, dated by the calendar day it happened on in the owner's local
/// time. Growth holds the length in centimetres with the value and unit the owner typed. Spawning holds the
/// generation of the offspring (1 for F1, 2 for F2 and so on) when the offspring is pure-bred, or marks the
/// offspring as a hybrid. A move holds the tank the fish left and the tank it entered. A death holds no
/// further columns.
/// </summary>
public class InhabitantEvent
{
    public long Id { get; set; }
    public long InhabitantId { get; set; }
    public InhabitantEventKind Kind { get; set; }
    public DateOnly OccurredOn { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? LengthInputValue { get; set; }
    public DimensionUnit? LengthInputUnit { get; set; }
    public int? OffspringGeneration { get; set; }
    public bool? OffspringHybrid { get; set; }
    public long? FromTankId { get; set; }
    public long? ToTankId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
