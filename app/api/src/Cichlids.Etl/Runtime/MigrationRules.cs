namespace Cichlids.Etl.Runtime;

/// <summary>
/// The migration rules the verification report names, one sentence each. A rule states what the
/// migration does with the legacy data, so a reader of the report sees why a count is expected.
/// The wording follows the agreed ETL hygiene rules of the legacy migration.
/// </summary>
public static class MigrationRules
{
    public const string Species =
        "Non-deleted legacy species migrate into the species catalog.";

    public const string SpeciesSlugs =
        "Species get their slug from the legacy species alias where one exists.";

    public const string MemberProfiles =
        "Non-deleted, enabled legacy accounts with at least one picture, tank or comment migrate as member profiles.";

    public const string ArchivedProfiles =
        "Content of a legacy owner without a member profile belongs to an unlisted archived placeholder profile.";

    public const string SystemProfiles =
        "Anonymous legacy content belongs to the community archive system profile.";

    public const string LegacyIdentities =
        "The Auth0, legacy OpenID and email logins of migrated members migrate as profile identities.";

    public const string OidcIdentities =
        "The account import links each imported Keycloak account to its member profile as an oidc identity.";

    public const string PrivateAddresses =
        "No public name of a profile, comment or forum post holds an email address.";

    public const string GravatarAvatars =
        "No profile avatar is a Gravatar URL, because a Gravatar URL is a hash of an email address.";

    public const string GuestNames =
        "A guest name holding an email address gets a generated guest name, the same one per address.";

    public const string Tanks =
        "Non-deleted legacy tanks migrate, including tanks without media.";

    public const string TankInhabitants =
        "The fish of a tank migrate as inhabitants where the legacy species resolves.";

    public const string DiscussionThreads =
        "Visible root messages of the three mapped Phorum forums migrate as discussion threads unless moved.";

    public const string DiscussionPosts =
        "Visible messages of a migrated thread migrate as discussion posts unless moved.";

    public const string DiscussionPostCounts =
        "The post count of a thread equals the number of its posts.";

    public const string MediaOriginals =
        "Every non-deleted legacy picture gets one original media item.";

    public const string ForumAttachments =
        "The attachments of migrated forum posts migrate into object storage.";

    public const string Stories =
        "Non-deleted pictures of the gallery and contest placements (legacy pid 21, 29, 109 and 131) become stories.";

    public const string StoryStates =
        "Gallery and contest pictures are published, unpublished uploads stay drafts, and stories of placeholder owners are archived.";

    public const string StorySubjects =
        "A legacy species link migrates once per story and species as a reference subject when both the picture and the species migrated.";

    public const string StoryItems =
        "Every story has its picture as a media link.";

    public const string StoryCovers =
        "The cover of a story, when set, is one of the item links of that story.";

    public const string SlugAliases =
        "Every legacy picture alias migrates as a unique slug alias of its story, with exactly one canonical alias per story.";

    public const string Comments =
        "A non-deleted legacy comment with text on a migrated story or tank becomes a comment.";

    public const string Ratings =
        "A member keeps one rating per story or tank: the newest legacy rating wins, a tie goes to the higher legacy uid, self-ratings are dropped and anonymous ratings migrate unchanged.";

    public const string NewestRatings =
        "Every migrated rating is an anonymous rating or the newest legacy rating of its member on its story or tank, a tie going to the higher legacy uid.";

    public const string RatingCommentReferences =
        "Every migrated rating whose legacy row also carries comment text references the migrated comment of that row.";

    public const string CollapsedRatings =
        "An older legacy rating of a member on a story or tank collapses into the member's newest rating there.";

    public const string SelfRatings =
        "A legacy rating of a member on their own picture or tank is dropped, while its comment text migrates.";

    public const string LegacyCommentVault =
        "A non-deleted legacy comment whose target is missing, not migrated or of an unknown type goes to the archive vault.";

    public const string CommentVotes =
        "A comment vote of +1 or -1 on a migrated comment migrates once per comment and member, the earliest vote winning.";

    public const string VoteTimes =
        "Legacy vote timestamps are America/New_York wall-clock times converted to UTC, so no vote precedes its comment.";

    public const string Aggregates =
        "Story rating counts and comment scores are recomputed from the migrated ratings and votes.";

    public const string Collections =
        "Non-deleted legacy galleries with at least one picture become collections.";

    public const string EmptyGalleries =
        "A non-deleted legacy gallery that never got a picture is skipped.";

    public const string CollectionEntries =
        "A gallery picture that migrated as a story becomes an entry of its collection, once per gallery and picture.";
}
