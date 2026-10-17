using System.Globalization;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Fish;

/// <summary>
/// Builds the timeline of a named fish from independent sources: the day it moved in, its stored events,
/// the anniversaries computed from the move-in day and its departure. Every source produces
/// FishTimelineItemDto values, and Order sorts all of them newest first. A source of another kind of item
/// adds its items to the list before Order is applied.
/// </summary>
internal static class FishTimeline
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string Format(DateOnly day) => day.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// The items that follow from the fish and its stored events. A tank the viewer may not see is absent
    /// from the tank lookup and shows as null in a move.
    /// </summary>
    public static List<FishTimelineItemDto> Items(
        Inhabitant fish, IReadOnlyList<InhabitantEvent> events, IReadOnlyDictionary<long, FishTankDto> tanks, DateOnly today)
    {
        var items = new List<FishTimelineItemDto>();
        if (fish.MovedInOn is { } movedIn)
        {
            items.Add(new FishTimelineItemDto(FishTimelineKind.MovedIn, Format(movedIn), null, null, null, null, null, null));
            var upTo = fish.RemovedOn ?? today;
            foreach (var (year, date) in Milestones.FishAnniversaries(movedIn, movedIn, upTo))
                items.Add(new FishTimelineItemDto(FishTimelineKind.Anniversary, Format(date), null, null, null, null, year, null));
        }

        foreach (var stored in events) items.Add(Item(stored, tanks));
        if (fish is { RemovedOn: { } removedOn, RemovalReason: { } reason } && reason != InhabitantRemovalReason.Died)
            items.Add(new FishTimelineItemDto(FishTimelineKind.Left, Format(removedOn), null, null, null, null, null, reason));
        return items;
    }

    /// <summary>
    /// Sorts items newest first. Items of one day follow the order of a fish's life: the death or departure
    /// comes first, then moves, then everything else, and the move-in comes last.
    /// </summary>
    public static List<FishTimelineItemDto> Order(IEnumerable<FishTimelineItemDto> items) =>
        items.OrderByDescending(i => i.On, StringComparer.Ordinal).ThenByDescending(i => Rank(i.Kind))
            .ThenByDescending(i => i.EventId ?? 0).ThenByDescending(i => i.Year ?? 0).ToList();

    private static int Rank(FishTimelineKind kind) => kind switch
    {
        FishTimelineKind.MovedIn => 0,
        FishTimelineKind.Moved => 2,
        FishTimelineKind.Died or FishTimelineKind.Left => 3,
        _ => 1,
    };

    private static FishTimelineItemDto Item(InhabitantEvent stored, IReadOnlyDictionary<long, FishTankDto> tanks)
    {
        var on = Format(stored.OccurredOn);
        return stored.Kind switch
        {
            InhabitantEventKind.Growth => new FishTimelineItemDto(FishTimelineKind.Growth, on, stored.Id,
                new FishGrowthDto(stored.LengthCm!.Value, stored.LengthInputValue!.Value, stored.LengthInputUnit!.Value), null, null, null, null),
            InhabitantEventKind.Spawning => new FishTimelineItemDto(FishTimelineKind.Spawning, on, stored.Id, null,
                new FishSpawningDto(stored.OffspringGeneration, stored.OffspringHybrid!.Value), null, null, null),
            InhabitantEventKind.Moved => new FishTimelineItemDto(FishTimelineKind.Moved, on, stored.Id, null, null,
                new FishMoveDto(tanks.GetValueOrDefault(stored.FromTankId!.Value), tanks.GetValueOrDefault(stored.ToTankId!.Value)), null, null),
            InhabitantEventKind.Died => new FishTimelineItemDto(FishTimelineKind.Died, on, stored.Id, null, null, null, null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(stored)),
        };
    }
}
