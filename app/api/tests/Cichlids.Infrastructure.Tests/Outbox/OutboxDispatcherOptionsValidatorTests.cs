using Cichlids.Infrastructure.Outbox;

namespace Cichlids.Infrastructure.Tests.Outbox;

public class OutboxDispatcherOptionsValidatorTests
{
    private readonly OutboxDispatcherOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsTheDefaults()
    {
        var options = new OutboxDispatcherOptions();

        Assert.True(_validator.Validate(null, options).Succeeded);
        Assert.Equal(TimeSpan.FromDays(7), options.GiveUpAfter);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ReservationLease);
        Assert.Equal(TimeSpan.FromSeconds(10), options.DeliveryTimeout);
        Assert.Equal(TimeSpan.FromHours(1), options.MaxBackoff);
    }

    public static TheoryData<Action<OutboxDispatcherOptions>, string> InvalidSettings => new()
    {
        { o => o.GiveUpAfter = TimeSpan.Zero, nameof(OutboxDispatcherOptions.GiveUpAfter) },
        { o => o.ReservationLease = TimeSpan.FromSeconds(-1), nameof(OutboxDispatcherOptions.ReservationLease) },
        { o => o.DeliveryTimeout = TimeSpan.Zero, nameof(OutboxDispatcherOptions.DeliveryTimeout) },
        { o => o.PollInterval = TimeSpan.Zero, nameof(OutboxDispatcherOptions.PollInterval) },
        { o => o.MaxBackoff = TimeSpan.Zero, nameof(OutboxDispatcherOptions.MaxBackoff) },
        { o => o.BatchSize = 0, nameof(OutboxDispatcherOptions.BatchSize) },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void Validate_RejectsSettingsThatAreNotPositive(Action<OutboxDispatcherOptions> configure, string setting)
    {
        var options = new OutboxDispatcherOptions();
        configure(options);

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(setting, result.FailureMessage);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(20, 10)]
    [InlineData(29, 10)]
    public void Validate_RejectsALeaseShorterThanThreeDeliveryTimeouts(int leaseSeconds, int timeoutSeconds)
    {
        var options = new OutboxDispatcherOptions
        {
            ReservationLease = TimeSpan.FromSeconds(leaseSeconds),
            DeliveryTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(nameof(OutboxDispatcherOptions.ReservationLease), result.FailureMessage);
    }

    [Fact]
    public void Validate_AcceptsALeaseOfThreeDeliveryTimeouts()
    {
        var options = new OutboxDispatcherOptions
        {
            ReservationLease = TimeSpan.FromSeconds(30),
            DeliveryTimeout = TimeSpan.FromSeconds(10),
        };

        Assert.True(_validator.Validate(null, options).Succeeded);
    }
}
