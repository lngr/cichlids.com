using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Domain;

/// <summary>
/// The limits of media links per context: a care entry holds at most six photos and two videos, a
/// water change session at most six evidence photos.
/// </summary>
public static class MediaLinkLimits
{
    public const int CarePhotosPerEntry = 6;
    public const int CareVideosPerEntry = 2;
    public const int EvidencePhotosPerSession = 6;
}

/// <summary>
/// Maps the sections of a tank to the roles of its media links.
/// </summary>
public static class MediaLinkRoles
{
    public static MediaLinkRole Of(TankMediaSection section) => section switch
    {
        TankMediaSection.Showcase => MediaLinkRole.Showcase,
        TankMediaSection.Decoration => MediaLinkRole.Decoration,
        TankMediaSection.Technic => MediaLinkRole.Technic,
        TankMediaSection.TimeLapse => MediaLinkRole.TimeLapse,
        TankMediaSection.Video => MediaLinkRole.Video,
        TankMediaSection.Decommission => MediaLinkRole.Decommission,
        TankMediaSection.Recommission => MediaLinkRole.Recommission,
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    public static TankMediaSection? SectionOf(MediaLinkRole role) => role switch
    {
        MediaLinkRole.Showcase => TankMediaSection.Showcase,
        MediaLinkRole.Decoration => TankMediaSection.Decoration,
        MediaLinkRole.Technic => TankMediaSection.Technic,
        MediaLinkRole.TimeLapse => TankMediaSection.TimeLapse,
        MediaLinkRole.Video => TankMediaSection.Video,
        MediaLinkRole.Decommission => TankMediaSection.Decommission,
        MediaLinkRole.Recommission => TankMediaSection.Recommission,
        _ => null,
    };
}

/// <summary>
/// Reusable query shapes over media links.
/// </summary>
public static class MediaLinkQueries
{
    /// <summary>
    /// The links of tanks that belong to a section of the tank: every tank link except the main image
    /// and the proof photos of a decommissioning, which belong to the decommissioning.
    /// </summary>
    public static IQueryable<MediaLink> TankSectionLinks(this IQueryable<MediaLink> links) =>
        links.Where(l => l.ContextType == MediaLinkContext.Tank && l.Role != MediaLinkRole.Main
            && l.Role != MediaLinkRole.Decommission && l.Role != MediaLinkRole.Recommission);

    /// <summary>
    /// The proof photo links of the decommissioning and recommissioning of tanks.
    /// </summary>
    public static IQueryable<MediaLink> TankProofLinks(this IQueryable<MediaLink> links) =>
        links.Where(l => l.ContextType == MediaLinkContext.Tank
            && (l.Role == MediaLinkRole.Decommission || l.Role == MediaLinkRole.Recommission));

    public static IQueryable<MediaLink> TankMainLinks(this IQueryable<MediaLink> links) =>
        links.Where(l => l.ContextType == MediaLinkContext.Tank && l.Role == MediaLinkRole.Main);

    public static IQueryable<MediaLink> PostLinks(this IQueryable<MediaLink> links) =>
        links.Where(l => l.ContextType == MediaLinkContext.Post);

    public static IQueryable<MediaLink> CareEntryLinks(this IQueryable<MediaLink> links, MediaLinkRole role) =>
        links.Where(l => l.ContextType == MediaLinkContext.CareEntry && l.Role == role);

    public static IQueryable<MediaLink> EvidenceLinks(this IQueryable<MediaLink> links) =>
        links.Where(l => l.ContextType == MediaLinkContext.WaterChangeSession);

    public static IQueryable<MediaLink> DiscussionPostLinks(this IQueryable<MediaLink> links) =>
        links.Where(l => l.ContextType == MediaLinkContext.DiscussionPost);

    public static IQueryable<MediaLink> ProfileLinks(this IQueryable<MediaLink> links, MediaLinkRole role) =>
        links.Where(l => l.ContextType == MediaLinkContext.Profile && l.Role == role);
}
