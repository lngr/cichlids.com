using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Cichlids.Api.Tests.Features.Uploads;

/// <summary>
/// A Postgres container, a RustFS container and an API host wired to both, for the upload tests
/// that assert on stored objects. The API's object store talks to the real S3 API of the RustFS
/// container, so every stored original and variant can be checked for existence and size. The
/// database starts empty apart from the migrations; each test creates the profiles and posts it
/// needs through the API itself.
/// </summary>
public sealed class UploadsFixture : IAsyncLifetime
{
    private const string ObjectStoreAccessKey = "cichlids";
    private const string ObjectStoreSecretKey = "cichlids-dev-secret"; // gitleaks:allow
    private const string ObjectStoreBucket = "cichlids-media-upload-test";
    private const int RustfsPort = 9000;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("cichlids")
        .WithUsername("cichlids")
        .WithPassword("cichlids")
        .Build();

    private readonly IContainer _rustfs = new ContainerBuilder("rustfs/rustfs:1.0.0-beta.8")
        .WithEnvironment("RUSTFS_ACCESS_KEY", ObjectStoreAccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", ObjectStoreSecretKey)
        .WithPortBinding(RustfsPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(RustfsPort))
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// A client for a second API host on the same database and object store whose configuration
    /// has no slug secret.
    /// </summary>
    public HttpClient ClientWithoutSlugSecret { get; private set; } = null!;

    private WebApplicationFactory<Program> _factoryWithoutSlugSecret = null!;

    /// <summary>
    /// The object store the API host writes to, for assertions on stored keys.
    /// </summary>
    public IObjectStore ObjectStore { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rustfs.StartAsync());

        var serviceUrl = $"http://{_rustfs.Hostname}:{_rustfs.GetMappedPublicPort(RustfsPort)}";

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Cichlids"] = _postgres.GetConnectionString(),
                    ["ObjectStorage:ServiceUrl"] = serviceUrl,
                    ["ObjectStorage:Region"] = "us-east-1",
                    ["ObjectStorage:Bucket"] = ObjectStoreBucket,
                    ["ObjectStorage:AccessKey"] = ObjectStoreAccessKey,
                    ["ObjectStorage:SecretKey"] = ObjectStoreSecretKey,
                    ["ObjectStorage:ForcePathStyle"] = "true",
                    ["ObjectStorage:PublicBaseUrl"] = $"{serviceUrl}/{ObjectStoreBucket}",
                    ["Authentication:Authority"] = TestTokens.Issuer,
                    ["Authentication:Audience"] = TestTokens.Audience,
                    ["Authentication:RequireHttpsMetadata"] = "false",
                    ["OutboxDispatcher:Enabled"] = "false",
                }));

            builder.ConfigureServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.TokenValidationParameters = TestTokens.ValidationParameters;
                }));
        });

        Client = _factory.CreateClient();

        _factoryWithoutSlugSecret = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?> { ["Slugs:Secret"] = string.Empty })));
        ClientWithoutSlugSecret = _factoryWithoutSlugSecret.CreateClient();

        await using (var context = CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        ObjectStore = _factory.Services.GetRequiredService<IObjectStore>();
        await EnsureBucketAsync((S3ObjectStore)ObjectStore);
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        ClientWithoutSlugSecret.Dispose();
        await _factoryWithoutSlugSecret.DisposeAsync();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rustfs.DisposeAsync();
    }

    public CichlidsDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention();

        return new CichlidsDbContext(optionsBuilder.Options);
    }

    // The container port opens before RustFS finishes initializing its S3 API, so the first
    // bucket call is retried until the API answers.
    private static async Task EnsureBucketAsync(S3ObjectStore store)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (true)
        {
            try
            {
                await store.EnsureBucketAsync();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class UploadsCollection : ICollectionFixture<UploadsFixture>
{
    public const string Name = "Uploads";
}
