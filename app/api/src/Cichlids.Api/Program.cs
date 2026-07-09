using System.Text.Json.Serialization;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Species;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(new SnakeCaseJsonNamingPolicy())));

builder.Services.AddCichlidsDbContext(builder.Configuration);
builder.Services.AddS3ObjectStore(builder.Configuration);

builder.Services.AddScoped<SpeciesQueryService>();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithName("GetHealth");

app.MapSpeciesEndpoints();

app.Run();

public partial class Program;
