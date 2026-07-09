using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cichlids.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CichlidsDbContext"/> against the "ConnectionStrings:Cichlids"
    /// connection string, with snake_case naming applied to every table and column. The
    /// connection string is read from <paramref name="configuration"/> when the context is first
    /// resolved rather than at registration time, so a host that overrides configuration after
    /// this call (for example a test host swapping in a different database) is honored.
    /// </summary>
    public static IServiceCollection AddCichlidsDbContext(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CichlidsDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString("Cichlids")
                ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:Cichlids.");

            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        });

        return services;
    }
}
