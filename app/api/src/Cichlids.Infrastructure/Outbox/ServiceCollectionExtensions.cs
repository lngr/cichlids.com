using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Cichlids.Infrastructure.Outbox;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox dispatch and redelivery services, the webhook target policy and the
    /// signed-delivery HTTP client. The dispatcher is configured from the "OutboxDispatcher"
    /// configuration section, validated at startup. The webhook targets are the already validated
    /// options of the "Webhooks" section. Scheduling the dispatch passes is left to the host.
    /// </summary>
    public static IServiceCollection AddOutboxDispatcher(
        this IServiceCollection services, IConfiguration configuration, WebhookTargetOptions webhookTargets)
    {
        services.AddOptions<OutboxDispatcherOptions>()
            .Bind(configuration.GetSection(OutboxDispatcherOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OutboxDispatcherOptions>, OutboxDispatcherOptionsValidator>();

        services.AddSingleton(new WebhookTargetPolicy(webhookTargets.AllowedTargetHosts));

        // Redirects are never followed, so a listed target cannot forward a delivery to a host
        // outside the allowlist.
        services.AddHttpClient(OutboxDispatchService.HttpClientName)
            .ConfigureHttpClient((serviceProvider, client) =>
                client.Timeout = serviceProvider.GetRequiredService<IOptions<OutboxDispatcherOptions>>().Value.DeliveryTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            });

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OutboxDispatchService>();
        services.AddScoped<OutboxRedelivery>();

        return services;
    }
}
