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
        group.MapGet("/{id:long}", GetByIdAsync).WithName("GetTank");
        group.MapGet("/{id:long}/comments", ListCommentsAsync).WithName("ListTankComments");
        group.MapPost("/{id:long}/comments", CreateCommentAsync).WithName("CreateTankComment")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

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
        var detail = await queryService.GetByIdAsync(tank.Id, cancellationToken);
        return TypedResults.Created($"/api/tanks/{tank.Id}", detail!);
    }

    private static async Task<Results<Ok<PagedResponse<TankListItemDto>>, BadRequest<string>>> ListAsync(
        TanksQueryService queryService, long? user, string? category, int? offset, int? limit, CancellationToken cancellationToken)
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
        var result = await queryService.ListAsync(user, categoryFilter, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<TankTimeLapseDto>, ProblemHttpResult>> GetTimeLapseAsync(
        TankTimeLapseQueryService queryService, long id, CancellationToken cancellationToken)
    {
        var result = await queryService.GetAsync(id, cancellationToken);
        return result is null
            ? ApiProblems.WithCode(StatusCodes.Status404NotFound, "Tank unavailable", "This tank is unavailable.", "tank_not_found")
            : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<TankDetailDto>, NotFound>> GetByIdAsync(
        TanksQueryService queryService, long id, CancellationToken cancellationToken)
    {
        var detail = await queryService.GetByIdAsync(id, cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    private static async Task<Results<Ok<PagedResponse<CommentDto>>, NotFound>> ListCommentsAsync(
        TanksQueryService tanksQueryService,
        CommentsQueryService commentsQueryService,
        long id,
        int? offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!await tanksQueryService.IsVisibleAsync(id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await commentsQueryService.ListForTankAsync(id, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<CommentCreatedDto>, BadRequest<string>, NotFound, ProblemHttpResult>> CreateCommentAsync(
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

        if (!await tanksQueryService.IsVisibleAsync(id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var author = await currentProfileService.ResolveAsync(user, cancellationToken);
        var created = await commentsWriteService.CreateAsync(postId: null, tankId: id, author, body, stars, cancellationToken);
        return created is null
            ? CommentsEndpoints.SelfRatingProblem()
            : TypedResults.Created($"/api/tanks/{id}/comments", created);
    }
}
