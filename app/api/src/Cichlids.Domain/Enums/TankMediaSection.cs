namespace Cichlids.Domain.Enums;

/// <summary>
/// Area of a tank a media item is attached to. Decommission holds the photo of the empty tank that
/// starts a decommissioning and Recommission the photo of the filled tank that ends it.
/// </summary>
public enum TankMediaSection
{
    Showcase,
    Decoration,
    Technic,
    TimeLapse,
    Video,
    Decommission,
    Recommission,
}
