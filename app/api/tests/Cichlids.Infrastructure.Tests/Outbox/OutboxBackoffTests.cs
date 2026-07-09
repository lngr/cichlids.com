using Cichlids.Infrastructure.Outbox;

namespace Cichlids.Infrastructure.Tests.Outbox;

public class OutboxBackoffTests
{
    [Fact]
    public void Compute_NoAttemptsYet_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, OutboxBackoff.Compute(0, TimeSpan.FromHours(1)));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    [InlineData(5, 80)]
    public void Compute_DoublesWithEachFailedAttempt(int attemptCount, int expectedSeconds)
    {
        var backoff = OutboxBackoff.Compute(attemptCount, TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), backoff);
    }

    [Fact]
    public void Compute_CapsAtTheConfiguredMaximum()
    {
        var backoff = OutboxBackoff.Compute(20, TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromHours(1), backoff);
    }
}
