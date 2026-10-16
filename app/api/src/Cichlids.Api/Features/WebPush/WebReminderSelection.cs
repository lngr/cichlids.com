using System.Globalization;
using Cichlids.Api.Features.Care;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.WebPush;

/// <summary>A snooze of one rule: the reminder for the due date returns on the until date.</summary>
public sealed record ReminderSnoozeRecord(long TankId, CareKind Kind, DateOnly DueDate, DateOnly UntilDate);

/// <summary>
/// A reminder to send: the rule of a tank, the due date it belongs to, and the local date on which
/// it falls (the due date, or the until date of a snooze).
/// </summary>
public sealed record WebReminder(long TankId, string TankTitle, CareKind Kind, DateOnly DueDate, DateOnly FireDate);

/// <summary>
/// Chooses the care reminders a browser receives at a moment of its own clock. A reminder falls at
/// 09:00 on the due day of an enabled rule whose care has been logged before; care that is overdue
/// gets none unless the member snoozed it, and a snooze moves the reminder of its due date to 09:00
/// on its until date. A tank with reminders switched off gets none.
/// </summary>
public static class WebReminderSelection
{
    /// <summary>The local hour at which a reminder falls.</summary>
    public const int ReminderHour = 9;

    /// <summary>
    /// The reminders that fall on the local date of the moment, once the clock has reached 09:00,
    /// ordered by tank and kind.
    /// </summary>
    public static IReadOnlyList<WebReminder> Select(IReadOnlyList<DueCareItemDto> items, IReadOnlyList<ReminderSnoozeRecord> snoozes, LocalMoment now)
    {
        if (now.TimeOfDay < TimeSpan.FromHours(ReminderHour)) return [];
        var reminders = new List<WebReminder>();
        foreach (var item in items)
        {
            if (!item.RemindersEnabled || item.Due.State == CareDueState.NeverLogged
                || !DateOnly.TryParseExact(item.Due.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate))
            {
                continue;
            }

            var snooze = snoozes.FirstOrDefault(s => s.TankId == item.TankId && s.Kind == item.Kind && s.DueDate == dueDate);
            DateOnly? fireDate = snooze?.UntilDate ?? (item.Due.State == CareDueState.DueToday ? dueDate : null);
            if (fireDate == now.Date) reminders.Add(new WebReminder(item.TankId, item.TankTitle, item.Kind, dueDate, now.Date));
        }

        return reminders.OrderBy(r => r.TankId).ThenBy(r => r.Kind).ToList();
    }
}
