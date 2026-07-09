using System.Text.Json.Serialization;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Me;
using Cichlids.Api.Features.Pictures;
using Cichlids.Api.Features.Profiles;
using Cichlids.Api.Features.Species;
using Cichlids.Api.Features.Tanks;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(new SnakeCaseJsonNamingPolicy())));

builder.Services.AddCichlidsDbContext(builder.Configuration);
builder.Services.AddS3ObjectStore(builder.Configuration);

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
    options.AddPolicy(AuthorizationPolicies.Moderator, policy => policy.RequireRole("moderator", "admin")));

builder.Services.AddScoped<SpeciesQueryService>();
builder.Services.AddScoped<PicturesQueryService>();
builder.Services.AddScoped<TanksQueryService>();
builder.Services.AddScoped<ProfilesQueryService>();
builder.Services.AddScoped<CommentsQueryService>();
builder.Services.AddScoped<CommentsWriteService>();
builder.Services.AddScoped<CurrentProfileService>();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

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

app.Run();

public partial class Program;
