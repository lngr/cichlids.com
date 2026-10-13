using Cichlids.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Webhooks;

/// <summary>
/// Runs outbox dispatch passes for the lifetime of the host. A pass that reserved a full batch is
/// followed by the next one at once; otherwise the loop waits for the poll interval. A failing
/// pass is logged and polling goes on, so the loop ends only when the host stops. The effective
/// webhook target allowlist is logged once at startup. The loop is switched off through
/// configuration in the shared test host, where tests run dispatch passes directly.
/// </summary>
public sealed class OutboxDispatcherHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxDispatcherOptions> options,
    WebhookTargetPolicy targetPolicy,
    ILogger<OutboxDispatcherHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Webhook target allowlist: {AllowedTargetHosts}",
            targetPolicy.AllowedHosts.Count == 0 ? "(empty)" : string.Join(", ", targetPolicy.AllowedHosts));

        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var reserved = 0;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatchService = scope.ServiceProvider.GetRequiredService<OutboxDispatchService>();
                reserved = await dispatchService.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch pass failed; polling continues");
            }

            if (reserved >= settings.BatchSize)
            {
                continue;
            }

            try
            {
                await Task.Delay(settings.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
