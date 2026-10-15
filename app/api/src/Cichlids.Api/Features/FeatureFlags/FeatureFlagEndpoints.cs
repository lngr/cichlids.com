namespace Cichlids.Api.Features.FeatureFlags;

public static class FeatureFlagEndpoints
{
    public static IEndpointRouteBuilder MapFeatureFlagEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/feature-flags", (FeatureFlagSettings settings, HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "public, max-age=30";
                return TypedResults.Ok(settings.Values);
            })
            .AllowAnonymous()
            .WithName("GetFeatureFlags")
            .WithTags("FeatureFlags");
        return app;
    }
}
