namespace Cichlids.Domain.Entities;

/// <summary>
/// A reminder of a member for one of their tanks, optionally created on a care entry. A to-do is
/// open until it has a done time and can be reopened.
/// </summary>
public class CareTodo
{
    public const int MaxTextLength = 200;

    public long Id { get; set; }
    public long ProfileId { get; set; }
    public long TankId { get; set; }

    /// <summary>
    /// The care entry the to-do was created on; null for a to-do without an entry.
    /// </summary>
    public long? CareEntryId { get; set; }

    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the to-do was marked done; null while it is open.
    /// </summary>
    public DateTimeOffset? DoneAt { get; set; }
}
