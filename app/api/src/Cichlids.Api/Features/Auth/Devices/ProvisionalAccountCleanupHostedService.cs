using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// Runs the removal of abandoned provisional accounts for the lifetime of the host, one pass per
/// configured interval. A failing pass is logged and the loop goes on, so it ends only when the
/// host stops.
/// </summary>
public sealed class ProvisionalAccountCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ProvisionalAccountCleanupOptions> options,
    ILogger<ProvisionalAccountCleanupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<ProvisionalAccountCleanupService>();
                var removed = await cleanup.RunOnceAsync(stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("Removed {Count} abandoned provisional account(s).", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Removal of abandoned provisional accounts failed; the next pass runs after the interval.");
            }

            try
            {
                await Task.Delay(settings.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
