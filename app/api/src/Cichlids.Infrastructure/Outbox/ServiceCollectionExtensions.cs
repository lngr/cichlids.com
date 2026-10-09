using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddOptions<OutboxDispatcherOptions>().Bind(configuration.GetSection(OutboxDispatcherOptions.SectionName));

        services.AddHttpClient(OutboxDispatchService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));

        services.AddScoped<OutboxDispatchService>();

        return services;
    }
}
