using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Tanks;

public static class TanksEndpoints
{
    public static IEndpointRouteBuilder MapTanksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tanks").WithTags("Tanks");

        group.MapPost("/", CreateAsync).WithName("CreateTank").RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapGet("/", ListAsync).WithName("ListTanks");
        group.MapGet("/{id:long}/time-lapse", GetTimeLapseAsync).WithName("GetTankTimeLapse")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:long}", GetByIdAsync).WithName("GetTank")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/{id:long}/visibility", SetVisibilityAsync).WithName("SetTankVisibility").RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:long}/comments", ListCommentsAsync).WithName("ListTankComments");
        group.MapPost("/{id:long}/comments", CreateCommentAsync).WithName("CreateTankComment")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapTankSeriesPhotoEndpoints();
        app.MapTankMediaEndpoints();

        return app;
    }

    private static async Task<Results<Created<TankDetailDto>, ProblemHttpResult>> CreateAsync(
        CreateTankRequest request,
        CurrentProfileService currentProfileService,
        TanksWriteService writeService,
        TanksQueryService queryService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (TanksWriteService.Validate(request) is { } error)
        {
            return ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Invalid tank", error.Detail, error.Code);
        }

        var author = await currentProfileService.ResolveAsync(user, cancellationToken);
        var tank = await writeService.CreateAsync(request, author.Id, cancellationToken);
        var detail = await queryService.GetByIdAsync(tank.Id, author.Id, cancellationToken);
        return TypedResults.Created($"/api/tanks/{tank.Id}", detail!);
    }

    private static async Task<Results<Ok<PagedResponse<TankListItemDto>>, BadRequest<string>>> ListAsync(
        TanksQueryService queryService, TankAccessService access, ClaimsPrincipal viewer, long? user, string? category, int? offset, int? limit,
        CancellationToken cancellationToken)
    {
        TankCategory? categoryFilter = null;
        if (category is not null)
        {
            if (!SnakeCaseEnum.TryParse<TankCategory>(category, out var parsedCategory))
            {
                return TypedResults.BadRequest($"Unknown category '{category}'.");
            }

            categoryFilter = parsedCategory;
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var viewerProfileId = await access.ViewerProfileIdAsync(viewer, cancellationToken);
        var result = await queryService.ListAsync(user, categoryFilter, viewerProfileId, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<TankTimeLapseDto>, ProblemHttpResult>> GetTimeLapseAsync(
        TankTimeLapseQueryService queryService, TankAccessService access, ClaimsPrincipal viewer, long id, CancellationToken cancellationToken)
    {
        var result = await queryService.GetAsync(id, cancellationToken, await access.ViewerProfileIdAsync(viewer, cancellationToken), allowSigned: true);
        return result is null ? NotFoundProblem() : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<TankDetailDto>, ProblemHttpResult>> GetByIdAsync(
        TanksQueryService queryService, TankAccessService access, ClaimsPrincipal viewer, long id, CancellationToken cancellationToken)
    {
        var detail = await queryService.GetByIdAsync(id, await access.ViewerProfileIdAsync(viewer, cancellationToken), cancellationToken);
        return detail is null ? NotFoundProblem() : TypedResults.Ok(detail);
    }

    private static async Task<Results<Ok<TankVisibilityDto>, ProblemHttpResult>> SetVisibilityAsync(
        long id, UpdateTankVisibilityRequest request, CurrentProfileService currentProfileService, TanksWriteService writeService,
        TankMediaPlacementService placement, Microsoft.Extensions.Options.IOptions<TankMediaPlacementOptions> placementOptions,
        ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var profile = await currentProfileService.ResolveAsync(user, cancellationToken);
        var (visibility, error) = await writeService.SetVisibilityAsync(id, profile.Id, request, cancellationToken);
        if (error is null && visibility!.Profile != TankVisibility.Public && placementOptions.Value.ImmediateBudget > TimeSpan.Zero)
        {
            // The media of a tank that turned private leave the public prefix before the answer, within the budget.
            await placement.PlaceNowAsync(id, placementOptions.Value.ImmediateBudget);
        }

        return error switch
        {
            null => TypedResults.Ok(visibility!),
            TanksWriteService.NotFoundCode => NotFoundProblem(),
            TanksWriteService.StreamWiderCode => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Visibility refused",
                "The care stream cannot be visible to more people than the tank profile.", error),
            _ => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Visibility refused",
                "Send the profile and the stream level as public or private.", error),
        };
    }

    private static ProblemHttpResult NotFoundProblem() =>
        ApiProblems.WithCode(StatusCodes.Status404NotFound, "Tank unavailable", "This tank is unavailable.", TanksWriteService.NotFoundCode);

    private static async Task<Results<Ok<PagedResponse<CommentDto>>, ProblemHttpResult>> ListCommentsAsync(
        TanksQueryService tanksQueryService,
        CommentsQueryService commentsQueryService,
        TankAccessService access,
        ClaimsPrincipal viewer,
        long id,
        int? offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!await tanksQueryService.IsVisibleAsync(id, await access.ViewerProfileIdAsync(viewer, cancellationToken), cancellationToken))
        {
            return NotFoundProblem();
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await commentsQueryService.ListForTankAsync(id, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<CommentCreatedDto>, BadRequest<string>, ProblemHttpResult>> CreateCommentAsync(
        TanksQueryService tanksQueryService,
        CommentsWriteService commentsWriteService,
        CurrentProfileService currentProfileService,
        ClaimsPrincipal user,
        long id,
        CreateCommentRequest request,
        CancellationToken cancellationToken)
    {
        if (!CommentsEndpoints.TryValidateCreate(request, out var body, out var stars, out var error))
        {
            return TypedResults.BadRequest(error);
        }

        if (CommentsEndpoints.IsBodyTooLong(body))
        {
            return CommentsEndpoints.CommentTooLongProblem();
        }

        var author = await currentProfileService.ResolveAsync(user, cancellationToken);
        if (!await tanksQueryService.IsVisibleAsync(id, author.Id, cancellationToken))
        {
            return NotFoundProblem();
        }

        var created = await commentsWriteService.CreateAsync(postId: null, tankId: id, author, body, stars, cancellationToken);
        return created is null
            ? CommentsEndpoints.SelfRatingProblem()
            : TypedResults.Created($"/api/tanks/{id}/comments", created);
    }
}
