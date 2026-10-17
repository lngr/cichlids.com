using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Streaks;
using Cichlids.Api.Features.Discover;
using System.Text.Json.Serialization;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Auth.Devices;
using Cichlids.Api.Features.Auth.WebSessions;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Community;
using Cichlids.Api.Features.Decommissioning;
using Cichlids.Api.Features.FeatureFlags;
using Cichlids.Api.Features.Fish;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Library;
using Cichlids.Api.Features.LegacyRedirects;
using Cichlids.Api.Features.Me;
using Cichlids.Api.Features.Onboarding;
using Cichlids.Api.Features.Retrospectives;
using Cichlids.Api.Features.Stories;
using Cichlids.Api.Features.Profiles;
using Cichlids.Api.Features.Species;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Features.Videos;
using Cichlids.Api.Features.WebPush;
using Cichlids.Api.Features.Webhooks;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Outbox;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Slugs;
using Cichlids.Infrastructure.Storage;
using Cichlids.Infrastructure.Video;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
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
                .WithExposedHeaders(VideoEndpoints.OffsetHeader)
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
// Resolved before the host is built so a deployment outside Development without the signing keys
// of the device tokens refuses to start.
var deviceTokenSettings = DeviceTokenSettings.Resolve(
    builder.Configuration, builder.Environment.EnvironmentName, builder.Environment.ContentRootPath, authenticationSettings.Authority);
builder.Services.AddSingleton(deviceTokenSettings);
builder.Services.AddSingleton<DeviceTokenIssuer>();
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

builder.Services.AddOptions<DeviceAuthOptions>()
    .Bind(builder.Configuration.GetSection(DeviceAuthOptions.SectionName))
    .Validate(
        o => o.ChallengeLifetime > TimeSpan.Zero && o.TimestampWindow > TimeSpan.Zero
            && o.MaxRegistrationsPerAddressPerHour > 0 && o.MaxTokenRequestsPerAddressPerMinute > 0,
        "DeviceAuth durations and limits must be positive.")
    .ValidateOnStart();
builder.Services.AddScoped<DeviceAuthService>();
builder.Services.AddScoped<ProvisionalRegistrationService>();
builder.Services.AddDeviceAuthRateLimits();

// The native app authenticates with a bearer token, the web app with its session cookie. A
// request with an Authorization header always uses the bearer token: a token of the device token
// issuer is validated as a device token, every other one as a Keycloak token.
const string bearerOrWebSessionScheme = "BearerOrWebSession";
builder.Services.AddAuthentication(bearerOrWebSessionScheme)
    .AddPolicyScheme(bearerOrWebSessionScheme, displayName: null, options =>
        options.ForwardDefaultSelector = context => WebSessionAuthenticationHandler.Applies(context, webSessionSettings)
            ? WebSessionAuthenticationHandler.SchemeName
            : DeviceTokenAuthentication.Applies(context, deviceTokenSettings)
                ? DeviceTokenAuthentication.SchemeName
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
    })
    .AddJwtBearer(DeviceTokenAuthentication.SchemeName, options => DeviceTokenAuthentication.Configure(options, deviceTokenSettings));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.Moderator, policy => policy.RequireRole("moderator", "admin"));
    options.AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole("admin"));
    options.AddPolicy(AuthorizationPolicies.Registered, policy =>
        policy.RequireAuthenticatedUser().AddRequirements(new RegisteredRequirement()));
});
builder.Services.AddSingleton<IAuthorizationHandler, RegisteredAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, RegistrationRequiredAuthorizationResultHandler>();

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (CareEndpoints.IsCarePath(context.HttpContext.Request.Path))
        context.ProblemDetails.Extensions.TryAdd("code", "care_request_invalid");
    else if (StreakEndpoints.IsStreakPath(context.HttpContext.Request.Path))
        context.ProblemDetails.Extensions.TryAdd("code", StreakEndpoints.RequestInvalidCode);
    else if (InhabitantEndpoints.IsInhabitantPath(context.HttpContext.Request.Path))
        context.ProblemDetails.Extensions.TryAdd("code", InhabitantEndpoints.RequestInvalidCode);
    else if (FishEndpoints.IsFishPath(context.HttpContext.Request.Path))
        context.ProblemDetails.Extensions.TryAdd("code", FishEndpoints.RequestInvalidCode);
    else if (WebPushEndpoints.IsWebPushPath(context.HttpContext.Request.Path))
        context.ProblemDetails.Extensions.TryAdd("code", "web_push_request_invalid");
    else if (TankSeriesPhotoEndpoints.IsSeriesPhotoPath(context.HttpContext.Request.Path))
        context.ProblemDetails.Extensions.TryAdd("code", "tank_photo_invalid");
    else if (TankDecommissioningEndpoints.IsDecommissioningPath(context.HttpContext.Request.Path))
        context.ProblemDetails.Extensions.TryAdd("code", TankDecommissioningEndpoints.RequestInvalidCode);
    else if (VideoEndpoints.IsVideoPath(context.HttpContext.Request.Path))
        context.ProblemDetails.Extensions.TryAdd("code", VideoProblem.RequestInvalid);
});
builder.Services.AddExceptionHandler<NonMemberLoginExceptionHandler>();
builder.Services.AddExceptionHandler<RegistrationRequiredExceptionHandler>();

builder.Services.AddScoped<SpeciesQueryService>();
builder.Services.AddScoped<SpeciesCommunityQueryService>();
builder.Services.AddScoped<StoriesQueryService>();
builder.Services.AddScoped<TanksQueryService>();
builder.Services.AddScoped<TankAccessService>();
builder.Services.AddScoped<TankFollowService>();
builder.Services.AddScoped<HandleChangeService>();
builder.Services.AddScoped<TankSelectedMemberService>();
builder.Services.AddOptions<MediaPlacementOptions>()
    .Bind(builder.Configuration.GetSection(MediaPlacementOptions.SectionName))
    .Validate(o => o.PollInterval > TimeSpan.Zero && o.Lease > TimeSpan.Zero && o.RetryDelay >= TimeSpan.Zero
        && o.ReconcileInterval > TimeSpan.Zero && o.ReconcileBatchSize > 0,
        "MediaPlacement durations must be positive.")
    .ValidateOnStart();
builder.Services.AddScoped<MediaPlacementService>();
builder.Services.AddScoped<MediaPlacementTrigger>();
builder.Services.AddHostedService<MediaPlacementHostedService>();
builder.Services.AddSingleton<MediaUrlSigner>();
builder.Services.AddScoped<TankTimeLapseQueryService>();
builder.Services.AddScoped<DiscoverQueryService>();
builder.Services.AddScoped<TodayQueryService>();
builder.Services.AddScoped<FollowedTanksService>();
builder.Services.AddScoped<MilestoneService>();
builder.Services.AddScoped<ReturnRetrospectiveService>();
builder.Services.AddScoped<IArchiveRetrospectiveGate, ArchiveRetrospectiveGate>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<TanksWriteService>();
builder.Services.AddScoped<InhabitantService>();
builder.Services.AddScoped<FishService>();
builder.Services.AddScoped<FishWriteService>();
builder.Services.AddScoped<CareWriteService>();
builder.Services.AddScoped<CarePhotoService>();
builder.Services.AddOptions<WaterChangeOptions>()
    .Bind(builder.Configuration.GetSection(WaterChangeOptions.SectionName))
    .Validate(o => o.MinDuration >= TimeSpan.Zero && o.SessionLifetime > TimeSpan.Zero && o.CleanupInterval > TimeSpan.Zero
        && o.ClockTolerance >= TimeSpan.Zero,
        "WaterChange durations must be positive.")
    .ValidateOnStart();
builder.Services.AddScoped<WaterChangeService>();
builder.Services.AddHostedService<WaterChangeCleanupHostedService>();
builder.Services.AddScoped<TankSeriesPhotoService>();
builder.Services.AddScoped<TankDecommissioningService>();
builder.Services.AddOptions<StreakOptions>()
    .Bind(builder.Configuration.GetSection(StreakOptions.SectionName))
    .Validate(o => o.FreezesPerYear is >= 0 and <= 12, "Streaks:FreezesPerYear must be between 0 and 12.")
    .ValidateOnStart();
builder.Services.AddScoped<StreakService>();
builder.Services.AddScoped<VacationService>();
builder.Services.AddScoped<CareTodoService>();
builder.Services.AddScoped<CareEntryMapper>();
builder.Services.AddScoped<CarePlanService>();
builder.Services.AddScoped<CareHeatmapService>();
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
builder.Services.AddScoped<ArchiveTankAdoptionService>();
builder.Services.AddScoped<ArchiveRetrospectiveQueryService>();
builder.Services.AddScoped<ProfilesQueryService>();
builder.Services.AddScoped<CommentsQueryService>();
builder.Services.AddScoped<CommentsWriteService>();
builder.Services.AddScoped<CurrentProfileService>();
builder.Services.AddScoped<MemberActivityService>();
builder.Services.AddSingleton<MemberActivityThrottle>();
builder.Services.AddScoped<CommunityQueryService>();
builder.Services.AddScoped<LegacyRedirectsQueryService>();
builder.Services.AddOptions<UploadLimitsOptions>()
    .Bind(builder.Configuration.GetSection(UploadLimitsOptions.SectionName))
    .Validate(o => o.MaxUploadsPerHour > 0 && o.MaxOpenDrafts > 0 && o.ProvisionalMaxUploadsPerHour > 0, "UploadLimits values must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<DraftCleanupOptions>()
    .Bind(builder.Configuration.GetSection(DraftCleanupOptions.SectionName))
    .Validate(
        o => o.Interval > TimeSpan.Zero && o.Retention > TimeSpan.Zero && o.BatchSize > 0,
        "DraftCleanup durations and batch size must be positive.")
    .ValidateOnStart();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddOptions<VideoLimitOptions>()
    .Bind(builder.Configuration.GetSection(VideoLimitOptions.SectionName))
    .Validate(
        o => o.MaxDuration > TimeSpan.Zero && o.ShortMaxDuration > TimeSpan.Zero && o.ShortMaxDuration <= o.MaxDuration
            && o.MinAccountAge >= TimeSpan.Zero && o.MinPublishedStories >= 0 && o.MaxBitrate > 0 && o.MaxOpenUploads > 0
            && o.ProvisionalMaxDuration > TimeSpan.Zero && o.ProvisionalMaxOpenUploads > 0 && o.ProvisionalMaxVideos > 0
            && o.ChunkSize >= 5 * 1024 * 1024 && o.UploadLifetime > TimeSpan.Zero && o.CleanupInterval > TimeSpan.Zero,
        "VideoLimits values must be positive, the short length must not exceed the long one and the chunk size must be at least 5 MiB.")
    .ValidateOnStart();
builder.Services.AddOptions<VideoProcessingOptions>()
    .Bind(builder.Configuration.GetSection(VideoProcessingOptions.SectionName))
    .Validate(
        o => o.PollInterval > TimeSpan.Zero && o.MaxAttempts > 0 && o.RetryDelay >= TimeSpan.Zero && o.Lease > TimeSpan.Zero,
        "VideoTranscoding values must be positive.")
    .ValidateOnStart();
var videoTools = builder.Configuration.GetSection(VideoToolOptions.SectionName).Get<VideoToolOptions>() ?? new VideoToolOptions();
if (videoTools.Threads < 1 || videoTools.Nice is < 0 or > 19 || videoTools.TranscodeTimeout <= TimeSpan.Zero || videoTools.ProbeTimeout <= TimeSpan.Zero)
{
    throw new InvalidOperationException("VideoTools needs at least one thread, a niceness from 0 to 19 and positive timeouts.");
}

builder.Services.AddSingleton(videoTools);
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<FfprobeClient>();
builder.Services.AddSingleton<PosterFrameSelector>();
builder.Services.AddSingleton<HlsTranscoder>();
builder.Services.AddScoped<VideoLimitPolicy>();
builder.Services.AddScoped<VideoUploadService>();
builder.Services.AddScoped<VideoUploadCleanupService>();
builder.Services.AddHostedService<VideoUploadCleanupHostedService>();
builder.Services.AddScoped<VideoJobQueue>();
builder.Services.AddScoped<VideoTranscodingService>();
builder.Services.AddHostedService<VideoTranscodingHostedService>();
builder.Services.AddScoped<VideoQueryService>();
builder.Services.AddScoped<VideoRemovalService>();
builder.Services.AddScoped<MediaLibraryQueryService>();
builder.Services.AddScoped<MediaLibraryService>();
builder.Services.AddScoped<ProfileMediaService>();
builder.Services.AddScoped<StoredObjectDeleter>();
builder.Services.AddScoped<UploadsWriteService>();
builder.Services.AddScoped<DraftCleanupService>();
builder.Services.AddHostedService<DraftCleanupHostedService>();
builder.Services.AddOptions<ProvisionalAccountCleanupOptions>()
    .Bind(builder.Configuration.GetSection(ProvisionalAccountCleanupOptions.SectionName))
    .Validate(
        o => o.Interval > TimeSpan.Zero && o.InactivityMonths > 0 && o.BatchSize > 0,
        "ProvisionalAccountCleanup interval, inactivity months and batch size must be positive.")
    .ValidateOnStart();
builder.Services.AddScoped<ProvisionalAccountCleanupService>();
builder.Services.AddHostedService<ProvisionalAccountCleanupHostedService>();
builder.Services.AddScoped<DraftsQueryService>();
builder.Services.AddScoped<StoryPublishService>();
builder.Services.AddScoped<StoryEditorQueryService>();
builder.Services.AddScoped<StoryEditorService>();

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

app.UseWhen(context => StreakEndpoints.IsStreakPath(context.Request.Path), branch =>
    branch.UseStatusCodePages(async context =>
    {
        var status = context.HttpContext.Response.StatusCode;
        await Cichlids.Api.Features.Common.ApiProblems.WithCode(status, "Streak request refused", "The streak request could not be completed.",
            status == 401 ? StreakEndpoints.AuthRequiredCode : StreakEndpoints.RequestInvalidCode).ExecuteAsync(context.HttpContext);
    }));

app.UseWhen(context => InhabitantEndpoints.IsInhabitantPath(context.Request.Path), branch =>
    branch.UseStatusCodePages(async context =>
    {
        var status = context.HttpContext.Response.StatusCode;
        await Cichlids.Api.Features.Common.ApiProblems.WithCode(status, "Stocking request refused", "The stocking request could not be completed.",
            status == 401 ? InhabitantEndpoints.AuthRequiredCode : InhabitantEndpoints.RequestInvalidCode).ExecuteAsync(context.HttpContext);
    }));

app.UseWhen(context => FishEndpoints.IsFishPath(context.Request.Path), branch =>
    branch.UseStatusCodePages(async context =>
    {
        var status = context.HttpContext.Response.StatusCode;
        await Cichlids.Api.Features.Common.ApiProblems.WithCode(status, "Fish request refused", "The fish request could not be completed.",
            status == 401 ? FishEndpoints.AuthRequiredCode : FishEndpoints.RequestInvalidCode).ExecuteAsync(context.HttpContext);
    }));

app.UseWhen(context => TankSeriesPhotoEndpoints.IsSeriesPhotoPath(context.Request.Path), branch =>
    branch.UseStatusCodePages(async context =>
    {
        var status = context.HttpContext.Response.StatusCode;
        await Cichlids.Api.Features.Common.ApiProblems.WithCode(status, "Tank photo refused", "The tank photo could not be added.",
            status == 401 ? "tank_photo_auth_required" : "tank_photo_invalid").ExecuteAsync(context.HttpContext);
    }));

app.UseWhen(context => TankDecommissioningEndpoints.IsDecommissioningPath(context.Request.Path), branch =>
    branch.UseStatusCodePages(async context =>
    {
        var status = context.HttpContext.Response.StatusCode;
        await Cichlids.Api.Features.Common.ApiProblems.WithCode(status, "Decommissioning request refused", "The decommissioning request could not be completed.",
            status == 401 ? TankDecommissioningEndpoints.AuthRequiredCode : TankDecommissioningEndpoints.RequestInvalidCode).ExecuteAsync(context.HttpContext);
    }));

app.UseWhen(context => VideoEndpoints.IsVideoPath(context.Request.Path), branch =>
    branch.UseStatusCodePages(async context =>
    {
        var status = context.HttpContext.Response.StatusCode;
        await Cichlids.Api.Features.Common.ApiProblems.WithCode(status, "Video request refused", "The video request could not be completed.",
            status == 401 ? VideoProblem.AuthRequired : VideoProblem.RequestInvalid).ExecuteAsync(context.HttpContext);
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
app.UseMemberActivity();
app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithName("GetHealth");

app.MapFeatureFlagEndpoints();
app.MapSpeciesEndpoints();
app.MapStoriesEndpoints();
app.MapStoryEditorEndpoints();
app.MapTanksEndpoints();
app.MapFishEndpoints();
app.MapCareEndpoints();
app.MapStreakEndpoints();
app.MapTankDecommissioningEndpoints();
app.MapWebPushEndpoints();
app.MapDiscoverEndpoints();
app.MapProfilesEndpoints();
app.MapCommentsEndpoints();
app.MapMeEndpoints();
app.MapRetrospectiveEndpoints();
app.MapMediaLibraryEndpoints();
app.MapProfileMediaEndpoints();
app.MapOnboardingEndpoints();
app.MapArchiveRetrospectiveEndpoints();
app.MapWebSessionEndpoints();
app.MapDeviceAuthEndpoints();
app.MapProvisionalRegistrationEndpoints();
app.MapCommunityEndpoints();
app.MapLegacyRedirectsEndpoints();
app.MapUploadsEndpoints();
app.MapVideoEndpoints();
app.MapMediaEndpoints();
app.MapWebhookAdminEndpoints();
app.MapOutboxAdminEndpoints();

app.Run();

public partial class Program;
