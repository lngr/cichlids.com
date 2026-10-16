using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Removes expired water change sessions with their photos at the configured interval for the
/// lifetime of the host. A failing pass is logged and the loop goes on.
/// </summary>
public sealed class WaterChangeCleanupHostedService(
    IServiceScopeFactory scopeFactory, IOptions<WaterChangeOptions> options, ILogger<WaterChangeCleanupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.CleanupEnabled) return;
        using var timer = new PeriodicTimer(settings.CleanupInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var removed = await scope.ServiceProvider.GetRequiredService<WaterChangeService>().ExpireAllAsync(stoppingToken);
                    if (removed > 0) logger.LogInformation("Water change cleanup removed {Count} expired session(s).", removed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Water change cleanup pass failed; the next pass runs after the interval");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping.
        }
    }
}
