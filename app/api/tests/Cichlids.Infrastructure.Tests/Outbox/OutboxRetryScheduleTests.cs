using Cichlids.Infrastructure.Outbox;

namespace Cichlids.Infrastructure.Tests.Outbox;

public class OutboxRetryScheduleTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly OutboxDispatcherOptions Options = new();

    [Fact]
    public void AfterFailure_CountsTheDelayFromTheAttempt()
    {
        var attemptedAt = OccurredAt.AddHours(3);

        var (nextAttemptAt, givenUp) = OutboxRetrySchedule.AfterFailure(1, attemptedAt, OccurredAt, Options);

        Assert.Equal(attemptedAt.AddSeconds(5), nextAttemptAt);
        Assert.False(givenUp);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(10, 2560)]
    [InlineData(11, 3600)]
    [InlineData(200, 3600)]
    public void AfterFailure_DoublesTheDelayUpToOneHour(int attemptCount, int expectedSeconds)
    {
        var attemptedAt = OccurredAt.AddMinutes(1);

        var (nextAttemptAt, _) = OutboxRetrySchedule.AfterFailure(attemptCount, attemptedAt, OccurredAt, Options);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), nextAttemptAt - attemptedAt);
    }

    [Fact]
    public void AfterFailure_KeepsRetryingJustBeforeTheGiveUpPeriodEndsRegardlessOfTheAttemptCount()
    {
        var attemptedAt = OccurredAt + TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1);

        var (_, givenUp) = OutboxRetrySchedule.AfterFailure(200, attemptedAt, OccurredAt, Options);

        Assert.False(givenUp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3600)]
    public void AfterFailure_GivesUpOnceTheGiveUpPeriodHasPassed(int secondsPastThePeriod)
    {
        var attemptedAt = OccurredAt + TimeSpan.FromDays(7) + TimeSpan.FromSeconds(secondsPastThePeriod);

        var (_, givenUp) = OutboxRetrySchedule.AfterFailure(3, attemptedAt, OccurredAt, Options);

        Assert.True(givenUp);
    }

    [Fact]
    public void AfterFailure_CountsTheGiveUpPeriodFromTheRedelivery()
    {
        var redeliveredAt = OccurredAt.AddDays(10);

        var (_, givenUpShortlyAfter) = OutboxRetrySchedule.AfterFailure(
            1, redeliveredAt.AddMinutes(1), redeliveredAt, Options);
        var (_, givenUpAfterTheNewPeriod) = OutboxRetrySchedule.AfterFailure(
            50, redeliveredAt.AddDays(7), redeliveredAt, Options);

        Assert.False(givenUpShortlyAfter);
        Assert.True(givenUpAfterTheNewPeriod);
    }

    [Fact]
    public void AfterFailure_UsesTheConfiguredGiveUpPeriod()
    {
        var options = new OutboxDispatcherOptions { GiveUpAfter = TimeSpan.FromSeconds(15) };

        var (_, givenUpBefore) = OutboxRetrySchedule.AfterFailure(2, OccurredAt.AddSeconds(14), OccurredAt, options);
        var (_, givenUpAfter) = OutboxRetrySchedule.AfterFailure(3, OccurredAt.AddSeconds(15), OccurredAt, options);

        Assert.False(givenUpBefore);
        Assert.True(givenUpAfter);
    }
}
