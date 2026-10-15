using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Tanks;

public static class TanksEndpoints
{
    public static IEndpointRouteBuilder MapTanksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tanks").WithTags("Tanks");

        group.MapPost("/", CreateAsync).WithName("CreateTank").RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapGet("/", ListAsync).WithName("ListTanks");
        group.MapGet("/{id:long}", GetByIdAsync).WithName("GetTank");
        group.MapGet("/{id:long}/comments", ListCommentsAsync).WithName("ListTankComments");
        group.MapPost("/{id:long}/comments", CreateCommentAsync).WithName("CreateTankComment")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Results<Created<TankDetailDto>, BadRequest<string>>> CreateAsync(
        CreateTankRequest request,
        CurrentProfileService currentProfileService,
        CichlidsDbContext context,
        TanksQueryService queryService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
        {
            return TypedResults.BadRequest("A title of at most 200 characters is required.");
        }
        if (request.Dimensions is { } dimensions &&
            (!Enum.IsDefined(dimensions.Unit) ||
             !ValidMeasurement(dimensions.Width) || !ValidMeasurement(dimensions.Height) || !ValidMeasurement(dimensions.Depth) ||
             (dimensions.Width is null && dimensions.Height is null && dimensions.Depth is null)))
        {
            return TypedResults.BadRequest("Dimensions require positive values and cm or inch.");
        }
        if (request.Volume is { } volume && (!Enum.IsDefined(volume.Unit) || !ValidMeasurement(volume.Value)))
        {
            return TypedResults.BadRequest("Volume requires a positive value and liter or us_gallon.");
        }
        var author = await currentProfileService.ResolveAsync(user, cancellationToken);
        var tank = new Tank
        {
            ProfileId = author.Id,
            Title = request.Title.Trim(),
            State = TankState.Published,
            CreatedAt = DateTimeOffset.UtcNow,
            PublishedAt = DateTimeOffset.UtcNow,
        };
        if (request.Dimensions is { } input)
        {
            TankMeasurements.SetDimensions(tank, input.Width, input.Height, input.Depth, input.Unit);
        }
        if (request.Volume is { } volumeInput)
        {
            TankMeasurements.SetVolume(tank, volumeInput.Value, volumeInput.Unit);
        }
        context.Tanks.Add(tank);
        await context.SaveChangesAsync(cancellationToken);
        var detail = await queryService.GetByIdAsync(tank.Id, cancellationToken);
        return TypedResults.Created($"/api/tanks/{tank.Id}", detail!);
    }

    private static bool ValidMeasurement(decimal? value) => value is null || value is > 0 and <= 1000000m;

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
