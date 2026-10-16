using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Discover;
using System.Text.Json.Serialization;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Auth.WebSessions;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Community;
using Cichlids.Api.Features.FeatureFlags;
using Cichlids.Api.Features.LegacyRedirects;
using Cichlids.Api.Features.Me;
using Cichlids.Api.Features.Onboarding;
using Cichlids.Api.Features.Pictures;
using Cichlids.Api.Features.Profiles;
using Cichlids.Api.Features.Species;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Features.WebPush;
using Cichlids.Api.Features.Webhooks;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Outbox;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Slugs;
using Cichlids.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(FeatureFlagSettings.Resolve(builder.Configuration));

// The Expo web target runs the app's browser build against this API from a
// different origin (Metro's dev server port); browsers enforce CORS for
// that cross-origin fetch even though native builds never hit this check.
// Credentials are allowed so the web session cookie reaches the API.
// Scoped to Development since only the local web dev server needs it; deployed
// environments serve the web app and the API from one origin.
const string localWebDevCorsPolicy = "LocalWebDev";
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
        options.AddPolicy(localWebDevCorsPolicy, policy =>
            policy.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
                && uri.Host is "localhost" or "127.0.0.1")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));
}

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(new SnakeCaseJsonNamingPolicy())));

builder.Services.AddCichlidsDbContext(builder.Configuration);
builder.Services.AddS3ObjectStore(builder.Configuration);
// Resolved before the host is built so an invalid webhook target allowlist fails startup with
// the validation message.
var webhookTargets = WebhookTargetOptions.Resolve(builder.Configuration);
builder.Services.AddOutboxDispatcher(builder.Configuration, webhookTargets);
builder.Services.AddHostedService<OutboxDispatcherHostedService>();

// Resolved before the authentication handler is registered so a deployment outside Development
// without an HTTPS authority or an audience refuses to start.
var authenticationSettings = AuthenticationSettings.Resolve(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddSingleton(authenticationSettings);
// Resolved before the host is built so a deployment outside Development with web session cookies
// or a callback that are not HTTPS refuses to start.
var webSessionSettings = WebSessionSettings.Resolve(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddSingleton(webSessionSettings);
// Resolved before the host is built so a deployment outside Development without the key ring
// that encrypts the stored web session tokens refuses to start.
var keyRingSettings = KeyRingSettings.Resolve(builder.Configuration, builder.Environment.EnvironmentName, builder.Environment.ContentRootPath);
builder.Services.AddKeyRing(keyRingSettings);
// Resolved before the host is built so a deployment outside Development that enables Web Push
// without the VAPID key pair refuses to start.
var webPushSettings = WebPushSettings.Resolve(builder.Configuration, builder.Environment.EnvironmentName, builder.Environment.ContentRootPath);
builder.Services.AddSingleton(webPushSettings);
builder.Services.AddHttpClient(KeycloakOidcClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<KeycloakOidcClient>();
builder.Services.AddSingleton<WebSessionLocks>();
builder.Services.AddSingleton<AccessTokenValidator>();
builder.Services.AddSingleton<WebSessionTokenProtector>();
builder.Services.AddScoped<WebSessionService>();
builder.Services.AddOptions<WebSessionCleanupOptions>()
    .Bind(builder.Configuration.GetSection(WebSessionCleanupOptions.SectionName))
    .Validate(
        o => o.Interval > TimeSpan.Zero && o.Retention > TimeSpan.Zero && o.BatchSize > 0,
        "WebSessionCleanup durations and batch size must be positive.")
    .ValidateOnStart();
builder.Services.AddScoped<WebSessionCleanupService>();
builder.Services.AddHostedService<WebSessionCleanupHostedService>();

// The native app authenticates with a bearer token, the web app with its session cookie. A
// request with an Authorization header always uses the bearer token.
const string bearerOrWebSessionScheme = "BearerOrWebSession";
builder.Services.AddAuthentication(bearerOrWebSessionScheme)
    .AddPolicyScheme(bearerOrWebSessionScheme, displayName: null, options =>
        options.ForwardDefaultSelector = context => WebSessionAuthenticationHandler.Applies(context, webSessionSettings)
            ? WebSessionAuthenticationHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, WebSessionAuthenticationHandler>(WebSessionAuthenticationHandler.SchemeName, configureOptions: null)
    .AddJwtBearer(options =>
    {
        options.Authority = authenticationSettings.Authority;
        options.RequireHttpsMetadata = authenticationSettings.RequireHttpsMetadata;
        // Claims keep their OIDC names (sub, preferred_username) instead of the legacy SOAP-era
        // claim type URIs the handler would map them to by default.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = authenticationSettings.Audience,
            NameClaimType = "preferred_username",
        };
        options.Events = new JwtBearerEvents { OnTokenValidated = KeycloakRealmRoles.PromoteToRoleClaims };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.Moderator, policy => policy.RequireRole("moderator", "admin"));
    options.AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole("admin"));
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (CareEndpoints.IsCarePath(context.HttpContext.Request.Path))
        context.ProblemDetails.Extensions.TryAdd("code", "care_request_invalid");
    else if (WebPushEndpoints.IsWebPushPath(context.HttpContext.Request.Path))
        context.ProblemDetails.Extensions.TryAdd("code", "web_push_request_invalid");
});
builder.Services.AddExceptionHandler<NonMemberLoginExceptionHandler>();

builder.Services.AddScoped<SpeciesQueryService>();
builder.Services.AddScoped<PicturesQueryService>();
builder.Services.AddScoped<TanksQueryService>();
builder.Services.AddScoped<TankTimeLapseQueryService>();
builder.Services.AddScoped<DiscoverQueryService>();
builder.Services.AddScoped<TodayQueryService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<TanksWriteService>();
builder.Services.AddScoped<CareWriteService>();
builder.Services.AddScoped<CarePhotoService>();
builder.Services.AddScoped<CareTodoService>();
builder.Services.AddScoped<CareEntryMapper>();
builder.Services.AddSingleton<CarePhotoUrlSigner>();
builder.Services.AddScoped<CarePlanService>();
builder.Services.AddScoped<WebPushSubscriptionService>();
builder.Services.AddScoped<ReminderSnoozeService>();
builder.Services.AddOptions<WebPushSenderOptions>()
    .Bind(builder.Configuration.GetSection(WebPushSenderOptions.SectionName))
    .Validate(o => o.Interval > TimeSpan.Zero, "WebPushSender interval must be positive.")
    .ValidateOnStart();
// The push services are reached without following redirects, so a push service cannot send a
// request to a host outside the allowlist.
builder.Services.AddHttpClient(WebPushTransport.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IWebPushTransport>(serviceProvider => webPushSettings.Available
    ? new WebPushTransport(serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(WebPushTransport.HttpClientName), webPushSettings)
    : new UnavailableWebPushTransport());
builder.Services.AddScoped<WebPushReminderService>();
builder.Services.AddHostedService<WebPushSenderHostedService>();
builder.Services.AddScoped<OnboardingWriteService>();
builder.Services.AddScoped<ProfilesQueryService>();
builder.Services.AddScoped<CommentsQueryService>();
builder.Services.AddScoped<CommentsWriteService>();
builder.Services.AddScoped<CurrentProfileService>();
builder.Services.AddScoped<CommunityQueryService>();
builder.Services.AddScoped<LegacyRedirectsQueryService>();
builder.Services.AddOptions<UploadLimitsOptions>()
    .Bind(builder.Configuration.GetSection(UploadLimitsOptions.SectionName))
    .Validate(o => o.MaxUploadsPerHour > 0 && o.MaxOpenDrafts > 0, "UploadLimits values must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<DraftCleanupOptions>()
    .Bind(builder.Configuration.GetSection(DraftCleanupOptions.SectionName))
    .Validate(
        o => o.Interval > TimeSpan.Zero && o.Retention > TimeSpan.Zero && o.BatchSize > 0,
        "DraftCleanup durations and batch size must be positive.")
    .ValidateOnStart();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddScoped<StoredObjectDeleter>();
builder.Services.AddScoped<UploadsWriteService>();
builder.Services.AddScoped<DraftCleanupService>();
builder.Services.AddHostedService<DraftCleanupHostedService>();
builder.Services.AddScoped<DraftsQueryService>();
builder.Services.AddScoped<PostPublishService>();

// Resolved once at startup so a deployment outside Development with no slug secret, or with the
// development default, fails immediately instead of on the first request that mints a slug,
// handle, login username or guest name.
var slugSecret = SlugSecretResolver.Resolve(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddSingleton(new SlugGenerator(slugSecret));
builder.Services.AddSingleton(new GeneratedNames(slugSecret));
builder.Services.AddSingleton(serviceProvider =>
    new Lazy<GeneratedNames>(serviceProvider.GetRequiredService<GeneratedNames>));

var app = builder.Build();

// Request errors raised by the host (oversized or malformed bodies) keep their own status code.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});

app.UseJsonRequestSizeLimit();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.UseCors(localWebDevCorsPolicy);
}

app.UseWhen(context => CareEndpoints.IsCarePath(context.Request.Path), branch =>
    branch.UseStatusCodePages(async context =>
    {
        var status = context.HttpContext.Response.StatusCode;
        await Cichlids.Api.Features.Common.ApiProblems.WithCode(status, "Care request refused", "The care request could not be completed.",
            status == 401 ? "care_auth_required" : "care_request_invalid").ExecuteAsync(context.HttpContext);
    }));

app.UseWhen(context => WebPushEndpoints.IsWebPushPath(context.Request.Path), branch =>
    branch.UseStatusCodePages(async context =>
    {
        var status = context.HttpContext.Response.StatusCode;
        await Cichlids.Api.Features.Common.ApiProblems.WithCode(status, "Web Push request refused", "The Web Push request could not be completed.",
            status == 401 ? "web_push_auth_required" : "web_push_request_invalid").ExecuteAsync(context.HttpContext);
    }));

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithName("GetHealth");

app.MapFeatureFlagEndpoints();
app.MapSpeciesEndpoints();
app.MapPicturesEndpoints();
app.MapTanksEndpoints();
app.MapCareEndpoints();
app.MapWebPushEndpoints();
app.MapDiscoverEndpoints();
app.MapProfilesEndpoints();
app.MapCommentsEndpoints();
app.MapMeEndpoints();
app.MapOnboardingEndpoints();
app.MapWebSessionEndpoints();
app.MapCommunityEndpoints();
app.MapLegacyRedirectsEndpoints();
app.MapUploadsEndpoints();
app.MapWebhookAdminEndpoints();
app.MapOutboxAdminEndpoints();

app.Run();

public partial class Program;
