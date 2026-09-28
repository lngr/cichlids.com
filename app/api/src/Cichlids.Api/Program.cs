using System.Text.Json.Serialization;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Community;
using Cichlids.Api.Features.LegacyRedirects;
using Cichlids.Api.Features.Me;
using Cichlids.Api.Features.Pictures;
using Cichlids.Api.Features.Profiles;
using Cichlids.Api.Features.Species;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Features.Webhooks;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Outbox;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Slugs;
using Cichlids.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// The Expo web target runs the app's browser build against this API from a
// different origin (Metro's dev server port); browsers enforce CORS for
// that cross-origin fetch even though native builds never hit this check.
// Scoped to Development since only the local web dev server needs it.
const string localWebDevCorsPolicy = "LocalWebDev";
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
        options.AddPolicy(localWebDevCorsPolicy, policy =>
            policy.SetIsOriginAllowed(origin => new Uri(origin).Host is "localhost" or "127.0.0.1")
                .AllowAnyHeader()
                .AllowAnyMethod()));
}

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(new SnakeCaseJsonNamingPolicy())));

builder.Services.AddCichlidsDbContext(builder.Configuration);
builder.Services.AddS3ObjectStore(builder.Configuration);
builder.Services.AddOutboxDispatcher(builder.Configuration);
builder.Services.AddHostedService<OutboxDispatcherHostedService>();

var authenticationSection = builder.Configuration.GetSection("Authentication");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authenticationSection["Authority"];
        options.RequireHttpsMetadata = authenticationSection.GetValue("RequireHttpsMetadata", true);
        // Claims keep their OIDC names (sub, preferred_username) instead of the legacy SOAP-era
        // claim type URIs the handler would map them to by default.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = authenticationSection["Audience"],
            NameClaimType = "preferred_username",
        };
        options.Events = new JwtBearerEvents { OnTokenValidated = KeycloakRealmRoles.PromoteToRoleClaims };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.Moderator, policy => policy.RequireRole("moderator", "admin"));
    options.AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole("admin"));
});

builder.Services.AddScoped<SpeciesQueryService>();
builder.Services.AddScoped<PicturesQueryService>();
builder.Services.AddScoped<TanksQueryService>();
builder.Services.AddScoped<ProfilesQueryService>();
builder.Services.AddScoped<CommentsQueryService>();
builder.Services.AddScoped<CommentsWriteService>();
builder.Services.AddScoped<CurrentProfileService>();
builder.Services.AddScoped<CommunityQueryService>();
builder.Services.AddScoped<LegacyRedirectsQueryService>();
builder.Services.AddScoped<UploadsWriteService>();
builder.Services.AddScoped<DraftsQueryService>();
builder.Services.AddScoped<PostPublishService>();

// Resolved on first use, so only the publish endpoint and a first login that needs a generated
// handle require a slug secret. The environment variable takes precedence over configuration, the
// same order the ETL uses, so migrated and new posts and profiles derive their generated values
// from the same secret.
builder.Services.AddSingleton(serviceProvider => new SlugGenerator(ResolveSlugSecret(serviceProvider)));
builder.Services.AddSingleton(serviceProvider => new GeneratedNames(ResolveSlugSecret(serviceProvider)));
builder.Services.AddSingleton(serviceProvider =>
    new Lazy<GeneratedNames>(serviceProvider.GetRequiredService<GeneratedNames>));

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.UseCors(localWebDevCorsPolicy);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithName("GetHealth");

app.MapSpeciesEndpoints();
app.MapPicturesEndpoints();
app.MapTanksEndpoints();
app.MapProfilesEndpoints();
app.MapCommentsEndpoints();
app.MapMeEndpoints();
app.MapCommunityEndpoints();
app.MapLegacyRedirectsEndpoints();
app.MapUploadsEndpoints();
app.MapWebhookAdminEndpoints();

app.Run();

static string ResolveSlugSecret(IServiceProvider serviceProvider)
{
    var secret = Environment.GetEnvironmentVariable(SlugGenerator.SecretEnvironmentVariable)
        ?? serviceProvider.GetRequiredService<IConfiguration>()[SlugGenerator.SecretConfigurationKey];

    return string.IsNullOrEmpty(secret)
        ? throw new InvalidOperationException(
            $"No slug secret configured: set {SlugGenerator.SecretEnvironmentVariable} or "
            + $"{SlugGenerator.SecretConfigurationKey}.")
        : secret;
}

public partial class Program;
