using Cichlids.Api.Features.Common;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Creates, lists, changes and deletes the to-dos of a member. Each write and its to-do event commit
/// in one transaction. The error of a write is a problem code.
/// </summary>
public sealed class CareTodoService(CichlidsDbContext context, TimeProvider clock)
{
    /// <summary>
    /// The to-dos a list shows: those still open, those marked done, or both.
    /// </summary>
    public enum TodoState { Open, Done, All }

    /// <summary>
    /// A text of 1 to 200 characters after trimming, or null when the text is not valid.
    /// </summary>
    public static string? NormalizeText(string? text) =>
        text?.Trim() is { Length: > 0 and <= CareTodo.MaxTextLength } trimmed ? trimmed : null;

    /// <summary>
    /// Creates a to-do on a tank of the member, on one of the member's care entries of that tank
    /// when an entry id is given.
    /// </summary>
    public async Task<(CareTodoDto? Todo, string? Error)> CreateAsync(
        long tankId, long? entryId, long profileId, string? text, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, "care_not_found");
        if (entryId is { } id && !await context.CareEntries.AnyAsync(e => e.Id == id && e.TankId == tankId && e.AuthorProfileId == profileId, ct))
            return (null, "care_not_found");
        if (NormalizeText(text) is not { } normalized) return (null, "todo_text_invalid");
        var now = clock.GetUtcNow();
        var todo = new CareTodo { ProfileId = profileId, TankId = tankId, CareEntryId = entryId, Text = normalized, CreatedAt = AtMicrosecondPrecision(now) };
        context.CareTodos.Add(todo);
        await context.SaveChangesAsync(ct);
        TodoEvents.Add(context, "todo.created", todo, now);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (CareTodoDto.From(todo), null);
    }

    /// <summary>
    /// The member's to-dos of their live tanks in the given state, with the total. Open ones come
    /// newest first, done ones by the time they were marked done, both newest first.
    /// </summary>
    public async Task<PagedResponse<TodoListItemDto>> ListAsync(long profileId, TodoState state, int offset, int limit, CancellationToken ct)
    {
        var query = context.CareTodos.AsNoTracking().Where(t => t.ProfileId == profileId
            && context.Tanks.Any(k => k.Id == t.TankId && k.DeletedAt == null)
            && (state == TodoState.All || (state == TodoState.Open) == (t.DoneAt == null)));
        var total = await query.CountAsync(ct);
        var ordered = state == TodoState.Done
            ? query.OrderByDescending(t => t.DoneAt).ThenByDescending(t => t.Id)
            : query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id);
        var items = await ordered.Skip(offset).Take(limit)
            .Select(t => new TodoListItemDto(t.Id, t.TankId, context.Tanks.Where(k => k.Id == t.TankId).Select(k => k.Title).First(),
                t.CareEntryId, t.Text, t.DoneAt != null, t.DoneAt, t.CreatedAt))
            .ToListAsync(ct);
        return new PagedResponse<TodoListItemDto>(total, items);
    }

    /// <summary>
    /// Marks a to-do of the member done or open and replaces its text. A request that changes nothing
    /// records no event.
    /// </summary>
    public async Task<(CareTodoDto? Todo, string? Error)> ChangeAsync(long id, long profileId, UpdateTodoRequest request, CancellationToken ct)
    {
        if (request.Done is null && request.Text is null) return (null, "todo_request_invalid");
        var text = request.Text is null ? null : NormalizeText(request.Text);
        if (request.Text is not null && text is null) return (null, "todo_text_invalid");
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var todo = await context.CareTodos.SingleOrDefaultAsync(t => t.Id == id && t.ProfileId == profileId, ct);
        if (todo is null) return (null, "todo_not_found");
        var now = clock.GetUtcNow();
        var changed = false;
        if (request.Done is { } done && done != (todo.DoneAt is not null))
        {
            todo.DoneAt = done ? AtMicrosecondPrecision(now) : null;
            changed = true;
        }
        if (text is not null && text != todo.Text)
        {
            todo.Text = text;
            changed = true;
        }
        if (changed)
        {
            TodoEvents.Add(context, "todo.updated", todo, now);
            await context.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return (CareTodoDto.From(todo), null);
    }

    /// <summary>
    /// Deletes a to-do of the member and returns its last state.
    /// </summary>
    public async Task<(CareTodoDto? Todo, string? Error)> RemoveAsync(long id, long profileId, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var todo = await context.CareTodos.SingleOrDefaultAsync(t => t.Id == id && t.ProfileId == profileId, ct);
        if (todo is null) return (null, "todo_not_found");
        TodoEvents.Add(context, "todo.deleted", todo, clock.GetUtcNow());
        context.CareTodos.Remove(todo);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (CareTodoDto.From(todo), null);
    }

    private static DateTimeOffset AtMicrosecondPrecision(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));
}
