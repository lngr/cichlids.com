using Cichlids.Infrastructure.Outbox;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
/// shared <see cref="ApiFixture"/> collection so a test can freely create webhook subscriptions
/// and drive dispatch passes without disturbing the shared read-endpoint seed. The hosted polling
/// dispatcher is disabled here too; every test drives <see cref="OutboxDispatchService.RunOnceAsync"/>
/// directly for deterministic assertions.
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
    /// Builds a dispatch service backed by its own database connection and the host's real
    /// HTTP client factory (so delivery goes over an actual loopback HTTP request to a
    /// <see cref="TestWebhookListener"/>), for a test to call <c>RunOnceAsync</c> on directly.
    /// Each call returns an independent instance so a test simulating two concurrent dispatcher
    /// processes gets two separate connections contending for the same rows.
    /// </summary>
    public OutboxDispatchService CreateDispatchService(OutboxDispatcherOptions? options = null) =>
        new(
            CreateDbContext(),
            _factory.Services.GetRequiredService<IHttpClientFactory>(),
            Options.Create(options ?? new OutboxDispatcherOptions()),
            NullLogger<OutboxDispatchService>.Instance);
}

[CollectionDefinition(Name)]
public sealed class OutboxDispatchCollection : ICollectionFixture<OutboxDispatchFixture>
{
    public const string Name = "OutboxDispatch";
}
