using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Care;

public static class TodoEndpoints
{
    public static IEndpointRouteBuilder MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tanks/{tankId:long}/care/{id:long}/todos", CreateOnEntryAsync).WithName("CreateCareEntryTodo").WithTags("Todos")
            .RequireAuthorization().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);
        app.MapPost("/api/tanks/{tankId:long}/todos", CreateOnTankAsync).WithName("CreateTankTodo").WithTags("Todos")
            .RequireAuthorization().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);
        var mine = app.MapGroup("/api/me/todos").WithTags("Todos").RequireAuthorization();
        mine.MapGet("", ListAsync).WithName("ListMyTodos").ProducesProblem(400).ProducesProblem(401);
        mine.MapPatch("/{id:long}", ChangeAsync).WithName("UpdateTodo").ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);
        mine.MapDelete("/{id:long}", RemoveAsync).WithName("DeleteTodo").ProducesProblem(401).ProducesProblem(404);
        return app;
    }

    private static async Task<Results<Created<CareTodoDto>, ProblemHttpResult>> CreateOnEntryAsync(
        long tankId, long id, CreateTodoRequest request, ClaimsPrincipal user, CurrentProfileService profiles, CareTodoService todos, CancellationToken ct)
    {
        var profile = await profiles.ResolveCallerAsync(user, ct);
        var (todo, error) = await todos.CreateAsync(tankId, id, profile.Id, request.Text, ct);
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.Created($"/api/me/todos?state=all", todo!);
    }

    private static async Task<Results<Created<CareTodoDto>, ProblemHttpResult>> CreateOnTankAsync(
        long tankId, CreateTodoRequest request, ClaimsPrincipal user, CurrentProfileService profiles, CareTodoService todos, CancellationToken ct)
    {
        var profile = await profiles.ResolveCallerAsync(user, ct);
        var (todo, error) = await todos.CreateAsync(tankId, null, profile.Id, request.Text, ct);
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.Created($"/api/me/todos?state=all", todo!);
    }

    private static async Task<Results<Ok<PagedResponse<TodoListItemDto>>, ProblemHttpResult>> ListAsync(
        string? state, int? offset, int? limit, ClaimsPrincipal user, CurrentProfileService profiles, CareTodoService todos, CancellationToken ct)
    {
        if (!SnakeCaseEnum.TryParse<CareTodoService.TodoState>(state ?? "open", out var parsed)) return CareEndpoints.Problem("todo_request_invalid");
        var profile = await profiles.ResolveCallerAsync(user, ct);
        var (skip, take) = Pagination.Normalize(offset, limit);
        return TypedResults.Ok(await todos.ListAsync(profile.Id, parsed, skip, take, ct));
    }

    private static async Task<Results<Ok<CareTodoDto>, ProblemHttpResult>> ChangeAsync(
        long id, UpdateTodoRequest request, ClaimsPrincipal user, CurrentProfileService profiles, CareTodoService todos, CancellationToken ct)
    {
        var profile = await profiles.ResolveCallerAsync(user, ct);
        var (todo, error) = await todos.ChangeAsync(id, profile.Id, request, ct);
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.Ok(todo!);
    }

    private static async Task<Results<Ok<CareTodoDto>, ProblemHttpResult>> RemoveAsync(
        long id, ClaimsPrincipal user, CurrentProfileService profiles, CareTodoService todos, CancellationToken ct)
    {
        var profile = await profiles.ResolveCallerAsync(user, ct);
        var (todo, error) = await todos.RemoveAsync(id, profile.Id, ct);
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.Ok(todo!);
    }
}
