using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// Attaches a media item to one context (a tank, an inhabitant, a care entry, a water change
/// session, a post, a discussion post or a profile). ContextType names the context and exactly the
/// key column that matches it is set, which the database checks. A link has a role within its
/// context, a position among the links of the same context and role and an optional caption that
/// belongs to this link only. Deleting the context or the media item deletes the link.
/// </summary>
public class MediaLink
{
    public long Id { get; set; }
    public long MediaItemId { get; set; }
    public MediaItem MediaItem { get; set; } = null!;
    public MediaLinkContext ContextType { get; set; }
    public long? TankId { get; set; }
    public long? InhabitantId { get; set; }
    public long? CareEntryId { get; set; }
    public long? WaterChangeSessionId { get; set; }
    public long? PostId { get; set; }
    public long? DiscussionPostId { get; set; }
    public long? ProfileId { get; set; }
    public MediaLinkRole Role { get; set; }
    public int Position { get; set; }
    public string? Caption { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static MediaLink ForTank(long tankId, long mediaItemId, MediaLinkRole role, int position = 0) =>
        new() { ContextType = MediaLinkContext.Tank, TankId = tankId, MediaItemId = mediaItemId, Role = role, Position = position };

    public static MediaLink ForCareEntry(long careEntryId, long mediaItemId, MediaLinkRole role, int position = 0) =>
        new() { ContextType = MediaLinkContext.CareEntry, CareEntryId = careEntryId, MediaItemId = mediaItemId, Role = role, Position = position };

    public static MediaLink ForWaterChangeSession(long sessionId, long mediaItemId, int position = 0) =>
        new() { ContextType = MediaLinkContext.WaterChangeSession, WaterChangeSessionId = sessionId, MediaItemId = mediaItemId, Role = MediaLinkRole.Evidence, Position = position };

    public static MediaLink ForPost(long postId, long mediaItemId, int position = 0) =>
        new() { ContextType = MediaLinkContext.Post, PostId = postId, MediaItemId = mediaItemId, Role = MediaLinkRole.Item, Position = position };

    public static MediaLink ForDiscussionPost(long discussionPostId, long mediaItemId, int position = 0) =>
        new() { ContextType = MediaLinkContext.DiscussionPost, DiscussionPostId = discussionPostId, MediaItemId = mediaItemId, Role = MediaLinkRole.Attachment, Position = position };

    public static MediaLink ForProfile(long profileId, long mediaItemId, MediaLinkRole role) =>
        new() { ContextType = MediaLinkContext.Profile, ProfileId = profileId, MediaItemId = mediaItemId, Role = role };
}
