using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Retrospectives;

public static class ArchiveRetrospectiveEndpoints
{
    public static IEndpointRouteBuilder MapArchiveRetrospectiveEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/retrospectives/archive", GetAsync)
            .WithName("GetMyArchiveRetrospective")
            .WithTags("Me")
            .RequireRegistered();

        app.MapPost("/api/me/retrospectives/archive/seen", MarkSeenAsync)
            .WithName("MarkMyArchiveRetrospectiveSeen")
            .WithTags("Me")
            .RequireRegistered();

        return app;
    }

    private static async Task<Results<Ok<ArchiveRetrospectiveDto>, NoContent>> GetAsync(
        ClaimsPrincipal user, CurrentProfileService profiles, ArchiveRetrospectiveQueryService retrospectives, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        return await retrospectives.GetAsync(profile, cancellationToken) is { } retrospective
            ? TypedResults.Ok(retrospective)
            : TypedResults.NoContent();
    }

    private static async Task<NoContent> MarkSeenAsync(
        ClaimsPrincipal user, CurrentProfileService profiles, CichlidsDbContext context, TimeProvider clock, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        var now = clock.GetUtcNow();
        await context.Profiles.Where(p => p.Id == profile.Id && p.ArchiveRetrospectiveSeenAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ArchiveRetrospectiveSeenAt, now), cancellationToken);
        return TypedResults.NoContent();
    }
}
