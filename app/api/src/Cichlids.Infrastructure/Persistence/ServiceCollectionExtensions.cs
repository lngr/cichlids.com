using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cichlids.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CichlidsDbContext"/> against the given PostgreSQL connection string,
    /// with snake_case naming applied to every table and column.
    /// </summary>
    public static IServiceCollection AddCichlidsDbContext(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CichlidsDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        return services;
    }
}
