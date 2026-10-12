using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Cichlids.Infrastructure.Outbox;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox dispatch and redelivery services, the webhook target policy and the
    /// signed-delivery HTTP client, configured from the "OutboxDispatcher" and "Webhooks"
    /// configuration sections, both validated at startup. Scheduling the dispatch passes is left
    /// to the host.
    /// </summary>
    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxDispatcherOptions>()
            .Bind(configuration.GetSection(OutboxDispatcherOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OutboxDispatcherOptions>, OutboxDispatcherOptionsValidator>();

        services.AddOptions<WebhookTargetOptions>()
            .Bind(configuration.GetSection(WebhookTargetOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WebhookTargetOptions>, WebhookTargetOptionsValidator>();
        services.AddSingleton(serviceProvider => new WebhookTargetPolicy(
            serviceProvider.GetRequiredService<IOptions<WebhookTargetOptions>>().Value.AllowedTargetHosts));

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
