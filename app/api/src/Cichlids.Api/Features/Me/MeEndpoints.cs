using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Profiles;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Me;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", GetAsync)
            .WithName("GetMe")
            .WithTags("Me")
            .RequireAuthorization();

        return app;
    }

    private static async Task<Ok<MeDto>> GetAsync(
        ClaimsPrincipal user,
        CurrentProfileService currentProfileService,
        ProfilesQueryService profilesQueryService,
        CancellationToken cancellationToken)
    {
        var profile = await currentProfileService.ResolveAsync(user, cancellationToken);

        var detail = await profilesQueryService.GetByIdAsync(profile.Id, cancellationToken)
            ?? throw new InvalidOperationException($"The resolved profile {profile.Id} has no public detail view.");

        var roles = user.Identity is ClaimsIdentity identity
            ? identity.FindAll(identity.RoleClaimType).Select(c => c.Value).Distinct().ToList()
            : [];

        return TypedResults.Ok(new MeDto(detail, roles));
    }
}
