using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.WebPush;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.WebPush;

/// <summary>
/// Which care reminders a browser receives: one at 09:00 on the due day of the browser's own clock
/// for every enabled rule that has been logged before, none for care that is overdue unless the
/// member snoozed it, none for tanks with reminders switched off.
/// </summary>
[Trait("Story", "task-5.12")]
public sealed class WebReminderSelectionTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly TimeZoneInfo Auckland = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DueCareItemDto Item(CareDueState state, string? dueDate, bool remindersEnabled = true, long tank = 1, CareKind kind = CareKind.WaterChange) =>
        new(tank, $"Tank {tank}", kind, 7, new CareReasonDto("cichlid_default", null), new CareDueDto(state, 0, dueDate), remindersEnabled);

    private static IReadOnlyList<WebReminder> Select(DateTimeOffset now, TimeZoneInfo zone, IReadOnlyList<DueCareItemDto> items,
        IReadOnlyList<ReminderSnoozeRecord>? snoozes = null) =>
        WebReminderSelection.Select(items, snoozes ?? [], LocalMoment.Of(now, zone));

    [Fact]
    public void A_rule_due_today_is_selected_from_nine_in_the_morning_of_the_browsers_clock()
    {
        var items = new[] { Item(CareDueState.DueToday, "2026-10-16") };

        Assert.Empty(Select(DateTimeOffset.Parse("2026-10-16T06:59:00Z"), Berlin, items));
        var reminder = Assert.Single(Select(DateTimeOffset.Parse("2026-10-16T07:00:00Z"), Berlin, items));
        Assert.Equal(new WebReminder(1, "Tank 1", CareKind.WaterChange, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 16)), reminder);
        Assert.Single(Select(DateTimeOffset.Parse("2026-10-16T21:59:00Z"), Berlin, items));
    }

    [Fact]
    public void The_same_instant_selects_by_the_local_clock_of_each_time_zone()
    {
        var items = new[] { Item(CareDueState.DueToday, "2026-10-16") };
        var instant = DateTimeOffset.Parse("2026-10-16T07:30:00Z");

        Assert.Single(Select(instant, Berlin, items));
        Assert.Single(Select(instant, Auckland, items));
        Assert.Empty(Select(instant, NewYork, items));
    }

    [Fact]
    public void The_local_day_of_the_browser_decides_when_the_zone_is_a_day_ahead()
    {
        var items = new[] { Item(CareDueState.DueToday, "2026-10-16") };
        var instant = DateTimeOffset.Parse("2026-10-15T22:30:00Z");

        Assert.Single(Select(instant, Auckland, items));
        Assert.Empty(Select(instant, Berlin, items));
    }

    [Fact]
    public void The_offset_of_the_clock_follows_daylight_saving_time()
    {
        var items = new[] { Item(CareDueState.DueToday, "2026-10-26") };

        // Daylight saving time ends on 2026-10-25: Berlin is on UTC+1, so 09:00 is 08:00 UTC.
        Assert.Empty(Select(DateTimeOffset.Parse("2026-10-26T07:59:00Z"), Berlin, items));
        Assert.Single(Select(DateTimeOffset.Parse("2026-10-26T08:00:00Z"), Berlin, items));
    }

    [Fact]
    public void Care_that_is_due_later_overdue_or_never_logged_is_not_selected()
    {
        var instant = DateTimeOffset.Parse("2026-10-16T08:00:00Z");

        Assert.Empty(Select(instant, Berlin, [Item(CareDueState.DueIn, "2026-10-18")]));
        Assert.Empty(Select(instant, Berlin, [Item(CareDueState.Overdue, "2026-10-14")]));
        Assert.Empty(Select(instant, Berlin, [Item(CareDueState.NeverLogged, null)]));
    }

    [Fact]
    public void A_tank_with_reminders_switched_off_is_not_selected()
    {
        var items = new[] { Item(CareDueState.DueToday, "2026-10-16", remindersEnabled: false), Item(CareDueState.DueToday, "2026-10-16", tank: 2) };

        var reminder = Assert.Single(Select(DateTimeOffset.Parse("2026-10-16T08:00:00Z"), Berlin, items));
        Assert.Equal(2, reminder.TankId);
    }

    [Fact]
    public void Every_rule_due_today_yields_its_own_reminder_in_a_stable_order()
    {
        var items = new[]
        {
            Item(CareDueState.DueToday, "2026-10-16", tank: 2, kind: CareKind.Measurement),
            Item(CareDueState.DueToday, "2026-10-16", tank: 2),
            Item(CareDueState.DueToday, "2026-10-16", tank: 1),
        };

        var reminders = Select(DateTimeOffset.Parse("2026-10-16T08:00:00Z"), Berlin, items);

        Assert.Equal([(1L, CareKind.WaterChange), (2L, CareKind.WaterChange), (2L, CareKind.Measurement)], reminders.Select(r => (r.TankId, r.Kind)));
    }

    [Fact]
    public void A_snooze_replaces_the_reminder_of_the_due_day_with_one_on_its_until_date()
    {
        var snooze = new ReminderSnoozeRecord(1, CareKind.WaterChange, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 17));

        Assert.Empty(Select(DateTimeOffset.Parse("2026-10-16T08:00:00Z"), Berlin, [Item(CareDueState.DueToday, "2026-10-16")], [snooze]));
        Assert.Empty(Select(DateTimeOffset.Parse("2026-10-17T06:59:00Z"), Berlin, [Item(CareDueState.Overdue, "2026-10-16")], [snooze]));
        var reminder = Assert.Single(Select(DateTimeOffset.Parse("2026-10-17T07:00:00Z"), Berlin, [Item(CareDueState.Overdue, "2026-10-16")], [snooze]));
        Assert.Equal(new DateOnly(2026, 10, 16), reminder.DueDate);
        Assert.Equal(new DateOnly(2026, 10, 17), reminder.FireDate);
        Assert.Empty(Select(DateTimeOffset.Parse("2026-10-18T08:00:00Z"), Berlin, [Item(CareDueState.Overdue, "2026-10-16")], [snooze]));
    }

    [Fact]
    public void A_snooze_of_an_earlier_due_date_does_not_apply_to_the_new_due_date()
    {
        var snooze = new ReminderSnoozeRecord(1, CareKind.WaterChange, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 10));

        Assert.Single(Select(DateTimeOffset.Parse("2026-10-16T08:00:00Z"), Berlin, [Item(CareDueState.DueToday, "2026-10-16")], [snooze]));
    }
}
