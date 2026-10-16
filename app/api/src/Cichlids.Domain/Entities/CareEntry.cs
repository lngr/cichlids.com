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

    /// <summary>
    /// The guided water change that created the entry; null for an entry logged without the mode.
    /// </summary>
    public long? WaterChangeSessionId { get; set; }

    /// <summary>
    /// Whole seconds the guided water change took; null without a session.
    /// </summary>
    public int? DurationSeconds { get; set; }

    /// <summary>
    /// The evidence state judged when the guided water change finished; null without a session.
    /// </summary>
    public WaterChangeEvidenceState? Evidence { get; set; }

    /// <summary>
    /// The finding codes that keep the evidence from being verified; empty otherwise.
    /// </summary>
    public List<string> EvidenceMissing { get; set; } = [];

    public List<CareMeasurementValue> Values { get; set; } = [];
    public List<CarePhoto> Photos { get; set; } = [];
    public List<CareVideo> Videos { get; set; } = [];
    public List<CareTodo> Todos { get; set; } = [];
}
