using Cichlids.Domain.Entities;

namespace Cichlids.Api.Features.Care;

public sealed record CareTodoDto(long Id, long TankId, long? CareEntryId, string Text, bool Done, DateTimeOffset? DoneAt, DateTimeOffset CreatedAt)
{
    public static CareTodoDto From(CareTodo todo) => new(todo.Id, todo.TankId, todo.CareEntryId, todo.Text, todo.DoneAt is not null,
        todo.DoneAt, todo.CreatedAt);
}
public sealed record TodoListItemDto(long Id, long TankId, string TankTitle, long? CareEntryId, string Text, bool Done,
    DateTimeOffset? DoneAt, DateTimeOffset CreatedAt);
public sealed record CreateTodoRequest(string? Text = null);

/// <summary>
/// The change of a to-do: mark it done or reopen it, and replace its text. At least one field is required.
/// </summary>
public sealed record UpdateTodoRequest(bool? Done = null, string? Text = null);
