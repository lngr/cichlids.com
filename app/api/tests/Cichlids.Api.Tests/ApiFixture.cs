using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Cichlids.Api.Tests;

/// <summary>
/// Starts one Postgres container and one <see cref="WebApplicationFactory{Program}"/> for the
/// whole test collection, applies the EF Core migrations once, and seeds the single shared
/// <see cref="SeedData"/> graph every read-endpoint test runs its assertions against. The object
/// store is configured but never reaches a real S3-compatible service, since building a public URL
/// is pure string formatting and no endpoint under test downloads or checks object existence.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private const string ObjectStorePublicBaseUrl = "http://objects.test/cichlids-media";

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
                    ["Authentication:Authority"] = TestTokens.Issuer,
                    ["Authentication:Audience"] = TestTokens.Audience,
                    ["Authentication:RequireHttpsMetadata"] = "false",
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
    /// that assert a variant or original resolved to a URL instead of null.
    /// </summary>
    public static string ExpectedPublicUrlPrefix => ObjectStorePublicBaseUrl;

    public CichlidsDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention();

        return new CichlidsDbContext(optionsBuilder.Options);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
