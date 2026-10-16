using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.WebPush;

/// <summary>
/// Runs the Web Push reminder sender at the configured interval for the lifetime of the host. It
/// does nothing while Web Push is unavailable. A failing pass is logged and the loop goes on, so it
/// ends only when the host stops.
/// </summary>
public sealed class WebPushSenderHostedService(
    IServiceScopeFactory scopeFactory,
    WebPushSettings settings,
    IOptions<WebPushSenderOptions> options,
    ILogger<WebPushSenderHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Available || !options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var pass = await scope.ServiceProvider.GetRequiredService<WebPushReminderService>().RunOnceAsync(stoppingToken);
                if (pass.Sent + pass.Removed + pass.Failed > 0)
                {
                    logger.LogInformation("Web Push pass sent {Sent}, removed {Removed} and failed {Failed} message(s).", pass.Sent, pass.Removed, pass.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Web Push pass failed; the next pass runs after the interval");
            }
        }
        while (await WaitForNextPassAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextPassAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
