using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cichlids.Infrastructure.Outbox;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox dispatch service and its signed-delivery HTTP client, configured from
    /// the "OutboxDispatcher" configuration section. This does not itself schedule any polling; a
    /// host (for example a BackgroundService) drives <see cref="OutboxDispatchService.RunOnceAsync"/>
    /// on whatever cadence fits its runtime.
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

        services.AddHttpClient(OutboxDispatchService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));

        services.AddScoped<OutboxDispatchService>();

        return services;
    }
}
