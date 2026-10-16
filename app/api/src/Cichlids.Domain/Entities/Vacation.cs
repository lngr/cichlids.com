namespace Cichlids.Domain.Entities;

/// <summary>
/// A vacation of a member in the member's local calendar dates, which pauses the streaks of all
/// tanks of the member. The first and the last day belong to the vacation; a vacation without an end
/// date is active.
/// </summary>
public class Vacation
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
