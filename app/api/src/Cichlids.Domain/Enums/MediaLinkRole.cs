namespace Cichlids.Domain.Enums;

/// <summary>
/// The part a media item plays within the context of its link. A tank has a main image, the
/// showcase, decoration and technic sections, the time-lapse series, videos and the proof photos of a
/// decommissioning (decommission) and of its end (recommission); a care entry has
/// photos and videos; a water change session has evidence photos; a post has items; a discussion
/// post has attachments; a profile has an avatar and a header.
/// </summary>
public enum MediaLinkRole
{
    Main,
    Showcase,
    Decoration,
    Technic,
    TimeLapse,
    Video,
    Decommission,
    Recommission,
    Photo,
    Evidence,
    Item,
    Attachment,
    Avatar,
    Header,
}
