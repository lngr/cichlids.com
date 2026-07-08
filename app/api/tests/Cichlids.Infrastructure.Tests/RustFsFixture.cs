using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Infrastructure.Tests;

/// <summary>
/// Starts one rustfs container for the whole test collection, using the same image and
/// credential environment variables as the local development stack. Every test creates its own
/// bucket so tests stay isolated without paying for a fresh container each time.
/// </summary>
public sealed class RustFsFixture : IAsyncLifetime
{
    private const string AccessKey = "cichlids";
    private const string SecretKey = "cichlids-dev-secret";
    private const int Port = 9000;

    private readonly IContainer _container = new ContainerBuilder("rustfs/rustfs:1.0.0-beta.8")
        .WithPortBinding(Port, assignRandomHostPort: true)
        .WithEnvironment("RUSTFS_ACCESS_KEY", AccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", SecretKey)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(Port))
        .Build();

    public string ServiceUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await WaitUntilS3ApiIsReadyAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Creates a store for a freshly named bucket and ensures the bucket exists, so each test
    /// gets an isolated namespace inside the shared container.
    /// </summary>
    public S3ObjectStore CreateStoreForNewBucket(int? listPageSize = null)
    {
        var options = new S3ObjectStoreOptions
        {
            ServiceUrl = ServiceUrl,
            Region = "us-east-1",
            Bucket = $"test-{Guid.NewGuid():N}",
            AccessKey = AccessKey,
            SecretKey = SecretKey,
            ForcePathStyle = true,
            PublicBaseUrl = "https://cdn.example.com/media",
            ListPageSize = listPageSize,
        };

        var client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config { ServiceURL = options.ServiceUrl, ForcePathStyle = options.ForcePathStyle, AuthenticationRegion = options.Region });

        var store = new S3ObjectStore(client, Options.Create(options));
        store.EnsureBucketAsync().GetAwaiter().GetResult();
        return store;
    }

    // The container port opens before rustfs finishes initializing its S3 API. Retrying a cheap
    // call absorbs that gap instead of racing every test against container startup.
    private async Task WaitUntilS3ApiIsReadyAsync()
    {
        using var client = new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config { ServiceURL = ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });

        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await client.ListBucketsAsync(new ListBucketsRequest());
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }

        throw new InvalidOperationException("rustfs did not become ready in time.", lastError);
    }
}

[CollectionDefinition(Name)]
public sealed class RustFsCollection : ICollectionFixture<RustFsFixture>
{
    public const string Name = "RustFs";
}
