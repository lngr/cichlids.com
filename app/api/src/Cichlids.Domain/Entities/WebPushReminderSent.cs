using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// The record that the reminder of one rule of a tank, for one due date and one local date, was
/// sent to one subscription. The unique key of the record makes each reminder go out once, however
/// many API instances run the sender.
/// </summary>
public class WebPushReminderSent
{
    public long Id { get; set; }
    public long SubscriptionId { get; set; }
    public long TankId { get; set; }
    public CareKind Kind { get; set; }
    public DateOnly DueDate { get; set; }

    /// <summary>The local date on which the reminder fell: the due date, or the until date of a snooze.</summary>
    public DateOnly FireDate { get; set; }

    public DateTimeOffset SentAt { get; set; }
}
