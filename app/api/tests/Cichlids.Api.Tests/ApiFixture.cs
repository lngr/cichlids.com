using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Cichlids.Api.Tests;

/// <summary>
/// Starts one Postgres container and one WebApplicationFactory for the
/// whole test collection, applies the EF Core migrations once, and seeds the single shared
/// SeedData graph every read-endpoint test runs its assertions against. The object
/// store is configured but never reaches a real S3-compatible service, since building a public URL
/// is pure string formatting and no endpoint under test downloads or checks object existence.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private const string ObjectStorePublicBaseUrl = "http://objects.test/cichlids-media";

    /// <summary>The VAPID key pair of the test API, generated once per test run.</summary>
    public static (string PublicKey, string PrivateKey) VapidKeyPair { get; } = Cichlids.Api.Features.WebPush.VapidKeys.Generate();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("cichlids")
        .WithUsername("cichlids")
        .WithPassword("cichlids")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// A client that does not follow redirects automatically, for tests that assert on a 301/410
    /// response and its Location header directly instead of on whatever the redirect target
    /// returns.
    /// </summary>
    public HttpClient NoRedirectClient { get; private set; } = null!;

    public SeedData Seed { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // The allowlist is read while the application is being built, so it is set as a host
            // setting. It overrides the Development allowlist index by index, so every index of
            // that list is set here.
            builder.UseSetting("Webhooks:AllowedTargetHosts:0", "example.test");
            builder.UseSetting("Webhooks:AllowedTargetHosts:1", "localhost");
            // Web Push is configured while the application is being built, like the allowlist above.
            // The sender loop stays off; tests run its passes directly.
            builder.UseSetting("WebPush:Enabled", "true");
            builder.UseSetting("WebPush:VapidPublicKey", VapidKeyPair.PublicKey);
            builder.UseSetting("WebPush:VapidPrivateKey", VapidKeyPair.PrivateKey);
            builder.UseSetting("WebPush:Subject", "mailto:operator@example.test");
            builder.UseSetting("WebPush:AllowedPushHosts:0", "push.example.test");
            builder.UseSetting("WebPushSender:Enabled", "false");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Cichlids"] = _postgres.GetConnectionString(),
                    ["ObjectStorage:ServiceUrl"] = "http://objects.test",
                    ["ObjectStorage:Region"] = "us-east-1",
                    ["ObjectStorage:Bucket"] = "cichlids-media",
                    ["ObjectStorage:AccessKey"] = "test",
                    ["ObjectStorage:SecretKey"] = "test",
                    ["ObjectStorage:ForcePathStyle"] = "true",
                    ["ObjectStorage:PublicBaseUrl"] = ObjectStorePublicBaseUrl,
                    // The polling dispatcher would otherwise race read-endpoint assertions that
                    // check an outbox row's dispatched_at right after a write; tests that exercise
                    // dispatch drive it directly instead.
                    ["OutboxDispatcher:Enabled"] = "false",
                    // Tests run the media placement directly instead of racing the worker.
                    ["TankMediaPlacement:Enabled"] = "false",
                    ["TankMediaPlacement:ImmediateBudget"] = "00:00:00",
                }));

            // Tokens are validated against the test signing key directly instead of an identity
            // provider's published metadata; clearing the authority prevents any metadata fetch.
            // Only the validation parameters are replaced, so the claim promotion configured in
            // the application (realm_access.roles into role claims) still runs.
            builder.ConfigureServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.TokenValidationParameters = TestTokens.ValidationParameters;
                }));
        });

        Client = _factory.CreateClient();
        NoRedirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();

        Seed = await SeedData.CreateAsync(context);
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        NoRedirectClient.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// The prefix every public media URL returned by the API is expected to start with, for tests
    /// that assert a variant resolved to a URL instead of null.
    /// </summary>
    public static string ExpectedPublicUrlPrefix => ObjectStorePublicBaseUrl;

    /// <summary>
    /// The application's service provider, for tests that compute an expected value with the
    /// same configured service the API uses.
    /// </summary>
    public IServiceProvider Services => _factory.Services;

    public CichlidsDbContext CreateDbContext() => CreateDbContext([]);

    /// <summary>
    /// Creates a context against the fixture database with the given EF Core interceptors
    /// attached, for tests that need to act at a precise point of a save.
    /// </summary>
    public CichlidsDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptors);

        return new CichlidsDbContext(optionsBuilder.Options);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
