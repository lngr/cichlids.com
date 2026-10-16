namespace Cichlids.Domain.Enums;

/// <summary>
/// The kind of place a media link attaches a media item to. Each context has its own key column in
/// the link table.
/// </summary>
public enum MediaLinkContext
{
    Tank,
    Inhabitant,
    CareEntry,
    WaterChangeSession,
    Post,
    DiscussionPost,
    Profile,
}
