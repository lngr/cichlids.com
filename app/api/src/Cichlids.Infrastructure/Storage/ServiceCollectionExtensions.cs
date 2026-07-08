using Amazon.S3;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cichlids.Infrastructure.Storage;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the object store abstraction backed by an S3-compatible service, configured
    /// from the "ObjectStorage" configuration section.
    /// </summary>
    public static IServiceCollection AddS3ObjectStore(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<S3ObjectStoreOptions>()
            .Bind(configuration.GetSection(S3ObjectStoreOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<S3ObjectStoreOptions>>().Value;

            var config = new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                ForcePathStyle = options.ForcePathStyle,
                AuthenticationRegion = options.Region,
            };

            var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
            return new AmazonS3Client(credentials, config);
        });

        services.AddSingleton<IObjectStore, S3ObjectStore>();

        return services;
    }
}
