using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// The routes that follow a tank and manage its audience: the follow relation of the caller, the
/// followers and follow requests of the owner, and the selected members of the owner.
/// </summary>
public static class TankFollowEndpoints
{
    public static IEndpointRouteBuilder MapTankFollowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tanks").WithTags("Tanks");

        group.MapGet("/{id:long}/follow", GetFollowAsync).WithName("GetTankFollow")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/{id:long}/follow", FollowAsync).WithName("FollowTank").RequireRegistered()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/{id:long}/follow", UnfollowAsync).WithName("UnfollowTank").RequireRegistered()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:long}/followers", ListFollowersAsync).WithName("ListTankFollowers").RequireRegistered()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/{id:long}/followers/{profileId:long}", RemoveFollowerAsync).WithName("RemoveTankFollower").RequireRegistered()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:long}/follow-requests", ListRequestsAsync).WithName("ListTankFollowRequests").RequireRegistered()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:long}/follow-requests/{profileId:long}/approve", ApproveAsync).WithName("ApproveTankFollowRequest").RequireRegistered()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:long}/follow-requests/{profileId:long}/decline", DeclineAsync).WithName("DeclineTankFollowRequest").RequireRegistered()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:long}/selected-members", ListSelectedAsync).WithName("ListTankSelectedMembers").RequireRegistered()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:long}/selected-members", AddSelectedAsync).WithName("AddTankSelectedMember").RequireRegistered()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/{id:long}/selected-members/{profileId:long}", RemoveSelectedAsync).WithName("RemoveTankSelectedMember").RequireRegistered()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    private static ProblemHttpResult NotFoundProblem() =>
        ApiProblems.WithCode(StatusCodes.Status404NotFound, "Tank unavailable", "This tank is unavailable.", TankFollowService.NotFoundCode);

    private static ProblemHttpResult Problem(string code) => code switch
    {
        TankFollowService.FollowNotAllowedCode => ApiProblems.WithCode(StatusCodes.Status409Conflict, "Follow refused",
            "This tank does not take followers from you.", code),
        TankFollowService.RequestNotFoundCode => ApiProblems.WithCode(StatusCodes.Status404NotFound, "Request unavailable",
            "There is no open follow request from this member.", code),
        TankFollowService.FollowerNotFoundCode => ApiProblems.WithCode(StatusCodes.Status404NotFound, "Follower unavailable",
            "This member does not follow the tank.", code),
        TankSelectedMemberService.HandleInvalidCode => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Member refused",
            "Send the handle of the member to select.", code),
        TankSelectedMemberService.ProfileNotFoundCode => ApiProblems.WithCode(StatusCodes.Status404NotFound, "Member unavailable",
            "There is no member with this handle.", code),
        TankSelectedMemberService.OwnerCode => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Member refused",
            "You see your own tank anyway.", code),
        TankSelectedMemberService.LimitCode => ApiProblems.WithCode(StatusCodes.Status409Conflict, "Member refused",
            $"A tank has at most {TankSelectedMemberService.MaxMembers} selected members.", code),
        _ => NotFoundProblem(),
    };

    private static async Task<Results<Ok<TankFollowDto>, ProblemHttpResult>> GetFollowAsync(
        long id, TankFollowService service, TankAccessService access, ClaimsPrincipal user, CancellationToken ct)
    {
        var result = await service.GetAsync(id, await access.ViewerProfileIdAsync(user, ct), ct);
        return result is null ? NotFoundProblem() : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<TankFollowDto>, ProblemHttpResult>> FollowAsync(
        long id, TankFollowService service, CurrentProfileService profiles, ClaimsPrincipal user, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (state, error) = await service.FollowAsync(id, profile.Id, ct);
        return error is not null ? Problem(error) : TypedResults.Ok(new TankFollowDto(state!.Value, CanFollow: false));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UnfollowAsync(
        long id, TankFollowService service, CurrentProfileService profiles, ClaimsPrincipal user, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        return await service.UnfollowAsync(id, profile.Id, ct) is { } error ? Problem(error) : TypedResults.NoContent();
    }

    private static Task<Results<Ok<PagedResponse<TankMemberDto>>, ProblemHttpResult>> ListFollowersAsync(
        long id, int? offset, int? limit, TankFollowService service, CurrentProfileService profiles, ClaimsPrincipal user, CancellationToken ct) =>
        ListAsync(id, FollowStatus.Approved, offset, limit, service, profiles, user, ct);

    private static Task<Results<Ok<PagedResponse<TankMemberDto>>, ProblemHttpResult>> ListRequestsAsync(
        long id, int? offset, int? limit, TankFollowService service, CurrentProfileService profiles, ClaimsPrincipal user, CancellationToken ct) =>
        ListAsync(id, FollowStatus.Pending, offset, limit, service, profiles, user, ct);

    private static async Task<Results<Ok<PagedResponse<TankMemberDto>>, ProblemHttpResult>> ListAsync(
        long id, FollowStatus status, int? offset, int? limit, TankFollowService service, CurrentProfileService profiles, ClaimsPrincipal user,
        CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await service.ListAsync(id, profile.Id, status, normalizedOffset, normalizedLimit, ct);
        return result is null ? NotFoundProblem() : TypedResults.Ok(result);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveFollowerAsync(
        long id, long profileId, TankFollowService service, CurrentProfileService profiles, ClaimsPrincipal user, CancellationToken ct)
    {
        var owner = await profiles.ResolveAsync(user, ct);
        return await service.RemoveFollowerAsync(id, owner.Id, profileId, ct) is { } error ? Problem(error) : TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ApproveAsync(
        long id, long profileId, TankFollowService service, CurrentProfileService profiles, ClaimsPrincipal user, CancellationToken ct)
    {
        var owner = await profiles.ResolveAsync(user, ct);
        return await service.ApproveAsync(id, owner.Id, profileId, ct) is { } error ? Problem(error) : TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeclineAsync(
        long id, long profileId, TankFollowService service, CurrentProfileService profiles, ClaimsPrincipal user, CancellationToken ct)
    {
        var owner = await profiles.ResolveAsync(user, ct);
        return await service.DeclineAsync(id, owner.Id, profileId, ct) is { } error ? Problem(error) : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<TankMemberDto>>, ProblemHttpResult>> ListSelectedAsync(
        long id, TankSelectedMemberService service, CurrentProfileService profiles, ClaimsPrincipal user, CancellationToken ct)
    {
        var owner = await profiles.ResolveAsync(user, ct);
        var result = await service.ListAsync(id, owner.Id, ct);
        return result is null ? NotFoundProblem() : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<TankMemberDto>, ProblemHttpResult>> AddSelectedAsync(
        long id, AddSelectedMemberRequest? request, TankSelectedMemberService service, CurrentProfileService profiles, ClaimsPrincipal user,
        CancellationToken ct)
    {
        var owner = await profiles.ResolveAsync(user, ct);
        var (member, error) = await service.AddAsync(id, owner.Id, request?.Handle, ct);
        return error is not null ? Problem(error) : TypedResults.Ok(member!);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveSelectedAsync(
        long id, long profileId, TankSelectedMemberService service, CurrentProfileService profiles, ClaimsPrincipal user, CancellationToken ct)
    {
        var owner = await profiles.ResolveAsync(user, ct);
        return await service.RemoveAsync(id, owner.Id, profileId, ct) is { } error ? Problem(error) : TypedResults.NoContent();
    }
}
