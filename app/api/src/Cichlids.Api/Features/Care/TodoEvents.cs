using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Records the outbox events of to-dos. An event holds the state of the to-do after the change, or
/// its last state for a deleted to-do.
/// </summary>
public static class TodoEvents
{
    public static void Add(CichlidsDbContext context, string type, CareTodo todo, DateTimeOffset now)
    {
        var payload = new
        {
            todoId = todo.Id, tankId = todo.TankId, profileId = todo.ProfileId, careEntryId = todo.CareEntryId, text = todo.Text,
            done = todo.DoneAt is not null, doneAt = todo.DoneAt, createdAt = todo.CreatedAt,
        };
        context.OutboxEvents.Add(OutboxEvent.Create(type, "todo", todo.Id.ToString(), JsonSerializer.Serialize(payload), now));
    }
}
