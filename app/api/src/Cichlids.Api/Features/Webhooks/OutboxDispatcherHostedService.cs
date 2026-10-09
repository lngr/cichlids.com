using Cichlids.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Webhooks;

/// <summary>
/// Drives <see cref="OutboxDispatchService"/> on a fixed polling interval for the lifetime of the
/// host. Disabled through configuration in the shared test host, where individual tests drive
/// dispatch passes directly for deterministic assertions instead of racing a timer.
/// </summary>
public sealed class OutboxDispatcherHostedService(
    IServiceScopeFactory scopeFactory, IOptions<OutboxDispatcherOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.PollInterval);
        do
        {
            using var scope = scopeFactory.CreateScope();
            var dispatchService = scope.ServiceProvider.GetRequiredService<OutboxDispatchService>();
            await dispatchService.RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
