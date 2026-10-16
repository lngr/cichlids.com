using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A care reminder a member moved to a later local day: the reminder of one rule of a tank for one
/// due date returns at 09:00 on the until date instead of the due day.
/// </summary>
public class CareReminderSnooze
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public long TankId { get; set; }
    public CareKind Kind { get; set; }

    /// <summary>The due date of the rule that the snooze belongs to.</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>The local date on which the reminder returns.</summary>
    public DateOnly UntilDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
