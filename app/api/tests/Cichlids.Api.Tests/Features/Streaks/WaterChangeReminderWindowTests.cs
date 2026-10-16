using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Streaks;

/// <summary>
/// The days on which a running water change streak reminds the member: from the last day of the
/// rhythm through the last day of the grace.
/// </summary>
[Trait("Story", "task-5.18")]
public sealed class WaterChangeReminderWindowTests
{
    private static readonly DateOnly Deadline = new(2026, 3, 21);

    [Theory]
    [InlineData(7, 3)]
    [InlineData(14, 5)]
    [InlineData(1, 1)]
    [InlineData(90, 30)]
    public void TheFirstReminderDayIsTheGraceBeforeTheDeadline(int rhythm, int days) =>
        Assert.Equal(Deadline.AddDays(-days), WaterChangeStreaks.FirstReminderDay(Deadline, rhythm));

    [Theory]
    [InlineData(7, 4)]
    [InlineData(14, 6)]
    public void TheReminderDaysAreTheGracePlusTheDueDay(int rhythm, int reminderDays) =>
        Assert.Equal(reminderDays, Deadline.DayNumber - WaterChangeStreaks.FirstReminderDay(Deadline, rhythm).DayNumber + 1);
}
