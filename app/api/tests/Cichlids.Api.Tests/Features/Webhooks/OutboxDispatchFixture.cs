using Cichlids.Infrastructure.Outbox;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Cichlids.Api.Tests.Features.Webhooks;

/// <summary>
/// A dedicated Postgres container and API host for outbox dispatch tests, separate from the
/// shared ApiFixture collection so a test can freely create webhook subscriptions and run
/// dispatch passes without disturbing the shared read-endpoint seed. The hosted polling
/// dispatcher is disabled here; every test calls the dispatch service directly for deterministic
/// assertions.
/// </summary>
public sealed class OutboxDispatchFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("cichlids")
        .WithUsername("cichlids")
        .WithPassword("cichlids")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // The allowlist is read while the application is being built, so it is set as a host
            // setting. It overrides the Development allowlist index by index, so every index of
            // that list is set here.
            builder.UseSetting("Webhooks:AllowedTargetHosts:0", "127.0.0.1");
            builder.UseSetting("Webhooks:AllowedTargetHosts:1", "localhost");
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
                    ["ObjectStorage:PublicBaseUrl"] = "http://objects.test/cichlids-media",
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

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public CichlidsDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention();

        return new CichlidsDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// The host's service provider, for tests that resolve the configured webhook target policy or
    /// HTTP client factory.
    /// </summary>
    public IServiceProvider Services => _factory.Services;

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>
    /// Builds a dispatch service backed by its own database connection and the host's real HTTP
    /// client factory and webhook target policy, so delivery goes over an actual loopback HTTP
    /// request to a TestWebhookListener. Each call returns an independent instance, so a test simulating
    /// several dispatcher processes gets separate connections contending for the same rows, each
    /// with its own clock.
    /// </summary>
    public OutboxDispatchService CreateDispatchService(
        OutboxDispatcherOptions? options = null, TimeProvider? timeProvider = null) =>
        new(
            CreateDbContext(),
            _factory.Services.GetRequiredService<IHttpClientFactory>(),
            _factory.Services.GetRequiredService<WebhookTargetPolicy>(),
            timeProvider ?? TimeProvider.System,
            Options.Create(options ?? new OutboxDispatcherOptions()),
            NullLogger<OutboxDispatchService>.Instance);

    /// <summary>
    /// Deletes every outbox event and webhook subscription, so a test starts without open events or
    /// subscribers left behind by an earlier test in the shared database.
    /// </summary>
    public async Task ResetOutboxAsync()
    {
        await using var context = CreateDbContext();
        await context.OutboxEvents.ExecuteDeleteAsync();
        await context.WebhookSubscriptions.ExecuteDeleteAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class OutboxDispatchCollection : ICollectionFixture<OutboxDispatchFixture>
{
    public const string Name = "OutboxDispatch";
}
