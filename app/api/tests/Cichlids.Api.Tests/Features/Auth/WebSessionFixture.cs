using Cichlids.Api.Features.Auth.WebSessions;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Cichlids.Api.Tests.Features.Auth;

/// <summary>
/// Runs the API against its own Postgres container with the web session settings of a deployed
/// environment (Secure cookies, an HTTPS callback, one web origin) and a FakeKeycloak in place of
/// the identity provider. Its client follows no redirects and keeps no cookies, so the tests read
/// and send every cookie explicitly.
/// </summary>
public sealed class WebSessionFixture : IAsyncLifetime
{
    public const string CallbackUrl = "https://cichlids.test/api/auth/callback";
    public const string WebOrigin = "https://cichlids.test";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("cichlids")
        .WithUsername("cichlids")
        .WithPassword("cichlids")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public FakeKeycloak Keycloak { get; } = new();

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WebSession:CallbackUrl", CallbackUrl);
            builder.UseSetting("WebSession:AllowedOrigins:0", WebOrigin);
            builder.UseSetting("WebSession:AllowedOrigins:1", WebOrigin);
            builder.UseSetting("WebSession:SecureCookies", "true");
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

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.TokenValidationParameters = TestTokens.ValidationParameters;
                });
                services.AddHttpClient(KeycloakOidcClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Keycloak);
            });
        });

        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(WebOrigin),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

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
}

[CollectionDefinition(Name)]
public sealed class WebSessionCollection : ICollectionFixture<WebSessionFixture>
{
    public const string Name = "WebSession";
}
