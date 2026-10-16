using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Library;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.Profiles;

/// <summary>
/// The avatar and the header of the signed-in member: choose a file of the media library, upload a fresh
/// photo or remove the link. Every route answers with the profile as the public profile route shows it.
/// </summary>
public static class ProfileMediaEndpoints
{
    public const string InvalidCode = "profile_media_invalid";

    public sealed record SetProfileMediaRequest(long? MediaItemId);

    public static IEndpointRouteBuilder MapProfileMediaEndpoints(this IEndpointRouteBuilder app)
    {
        MapRole(app, "avatar", "Avatar", MediaLinkRole.Avatar);
        MapRole(app, "header", "Header", MediaLinkRole.Header);
        return app;
    }

    private static void MapRole(IEndpointRouteBuilder app, string route, string name, MediaLinkRole role)
    {
        app.MapPut($"/api/me/{route}", (SetProfileMediaRequest? request, ClaimsPrincipal user, CurrentProfileService profiles,
                ProfileMediaService service, ProfilesQueryService query, CancellationToken ct) =>
                SetAsync(role, request, user, profiles, service, query, ct))
            .WithName($"SetMy{name}").WithTags("Me").RequireRegistered()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        // Callers authenticate with a bearer token, never a cookie, so antiforgery validation has nothing to protect.
        app.MapPost($"/api/me/{route}/photo", ([FromForm] IFormFile? file, ClaimsPrincipal user, CurrentProfileService profiles,
                ProfileMediaService service, ProfilesQueryService query, CancellationToken ct) =>
                UploadAsync(role, file, user, profiles, service, query, ct))
            .WithName($"UploadMy{name}").WithTags("Me").RequireRegistered()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadsEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: UploadsEndpoints.MaxRequestBytes)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests);
        app.MapDelete($"/api/me/{route}", (ClaimsPrincipal user, CurrentProfileService profiles, ProfileMediaService service,
                ProfilesQueryService query, CancellationToken ct) => RemoveAsync(role, user, profiles, service, query, ct))
            .WithName($"RemoveMy{name}").WithTags("Me").RequireRegistered();
    }

    private static async Task<Results<Ok<ProfileDetailDto>, ProblemHttpResult>> SetAsync(
        MediaLinkRole role, SetProfileMediaRequest? request, ClaimsPrincipal user, CurrentProfileService profiles,
        ProfileMediaService service, ProfilesQueryService query, CancellationToken cancellationToken)
    {
        if (request?.MediaItemId is not { } mediaItemId)
        {
            return ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Invalid file",
                "Send the id of a photo in your media library as mediaItemId.", InvalidCode);
        }

        var profile = await profiles.ResolveAsync(user, cancellationToken);
        var error = await service.SetAsync(profile.Id, role, mediaItemId, cancellationToken);
        return error is null ? await DetailAsync(query, profile.Id, cancellationToken) : ErrorProblem(error);
    }

    private static async Task<Results<Ok<ProfileDetailDto>, ProblemHttpResult>> UploadAsync(
        MediaLinkRole role, IFormFile? file, ClaimsPrincipal user, CurrentProfileService profiles, ProfileMediaService service,
        ProfilesQueryService query, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        var error = await service.SetFromPhotoAsync(profile.Id, role, file, cancellationToken);
        return error is null ? await DetailAsync(query, profile.Id, cancellationToken) : ErrorProblem(error);
    }

    private static async Task<Ok<ProfileDetailDto>> RemoveAsync(
        MediaLinkRole role, ClaimsPrincipal user, CurrentProfileService profiles, ProfileMediaService service, ProfilesQueryService query,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        await service.RemoveAsync(profile.Id, role, cancellationToken);
        return await DetailAsync(query, profile.Id, cancellationToken);
    }

    private static async Task<Ok<ProfileDetailDto>> DetailAsync(ProfilesQueryService query, long profileId, CancellationToken cancellationToken) =>
        TypedResults.Ok(await query.GetByIdAsync(profileId, profileId, cancellationToken)
            ?? throw new InvalidOperationException($"The resolved profile {profileId} has no public detail view."));

    private static ProblemHttpResult ErrorProblem(string error) => error switch
    {
        MediaLibraryService.NotFoundCode => ApiProblems.WithCode(StatusCodes.Status404NotFound, "File unavailable",
            "This file does not exist in your media library.", error),
        ProfileMediaService.NotPhotoCode => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Photo required",
            "Only a photo can be an avatar or a header.", error),
        UploadsEndpoints.UploadRateLimitedCode => UploadsEndpoints.RateLimitProblem(),
        _ => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Photo refused",
            "Send one JPEG, PNG or WebP photo of up to 25 MB that can be decoded.", error),
    };
}
