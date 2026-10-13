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
/// Runs the API against its own Postgres container and key ring with the web session settings of
/// a deployed environment (Secure cookies, an HTTPS callback, one web origin) and a FakeKeycloak
/// in place of the identity provider. Its client follows no redirects and keeps no cookies, so the tests read
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

    private readonly DirectoryInfo _keyRing = Directory.CreateTempSubdirectory("cichlids-web-session-keys-");

    private WebApplicationFactory<Program> _factory = null!;

    public FakeKeycloak Keycloak { get; } = new();

    public HttpClient Client { get; private set; } = null!;

    /// <summary>The services of the API host, for resolving the services a test runs directly.</summary>
    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WebSession:CallbackUrl", CallbackUrl);
            builder.UseSetting("WebSession:AllowedOrigins:0", WebOrigin);
            builder.UseSetting("WebSession:AllowedOrigins:1", WebOrigin);
            builder.UseSetting("WebSession:SecureCookies", "true");
            builder.UseSetting("DataProtection:KeyRingPath", _keyRing.FullName);
            builder.UseSetting("WebSession:ClientId", FakeKeycloak.ClientId);
            builder.UseSetting("WebSession:ClientSecret", FakeKeycloak.ClientSecret);
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
                    ["WebSessionCleanup:Enabled"] = "false",
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
        _keyRing.Delete(recursive: true);
    }

    /// <summary>The plain text of a token column, decrypted with the key ring of the API.</summary>
    public string Decrypt(string encrypted) =>
        Services.GetRequiredService<WebSessionTokenProtector>().TryDecrypt(encrypted, out var token)
            ? token
            : throw new InvalidOperationException("The token cannot be decrypted with the key ring of the API.");

    /// <summary>A token encrypted with the key ring of the API, as the API stores it.</summary>
    public string Encrypt(string token) => Services.GetRequiredService<WebSessionTokenProtector>().Encrypt(token);

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
