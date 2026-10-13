using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>
/// Runs web session cleanup passes for the lifetime of the host. A pass that deleted a full batch
/// is followed by the next one at once; otherwise the loop waits for the configured interval. A
/// failing pass is logged and the loop goes on, so it ends only when the host stops.
/// </summary>
public sealed class WebSessionCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<WebSessionCleanupOptions> options,
    ILogger<WebSessionCleanupHostedService> logger) : BackgroundService
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
            var deleted = 0;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<WebSessionCleanupService>();
                deleted = await cleanup.RunOnceAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Web session cleanup deleted {Count} unused session(s).", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Web session cleanup pass failed; the next pass runs after the interval");
            }

            if (deleted >= settings.BatchSize)
            {
                continue;
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
