using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;

namespace Cichlids.Api.Tests;

/// <summary>
/// The one hand-built graph every read-endpoint test runs against: two member profiles and one
/// archived profile, pictures in every visibility state with canonical and alias slugs, a tank
/// with all three media sections and inhabitants, a species with full care data, and comments
/// both with and without a resolvable author.
/// </summary>
public sealed record SeedData(
    Profile Alice,
    Profile Bob,
    Profile ArchivedProfile,
    Species Tropheus,
    Species Neolamprologus,
    Post PublishedPictureA,
    string PublishedPictureACanonicalSlug,
    string PublishedPictureAAltSlug,
    Post PublishedPictureB,
    string PublishedPictureBCanonicalSlug,
    Post PublishedPictureCHighRatingLowCount,
    string PublishedPictureCSlug,
    Post OfftopicPicture,
    string OfftopicPictureSlug,
    Post DraftPicture,
    string DraftPictureSlug,
    Post ArchivedPicture,
    string ArchivedPictureSlug,
    Post DeletedPicture,
    string DeletedPictureSlug,
    Comment PictureCommentWithAuthor,
    Comment PictureCommentAnonymous,
    Tank PublishedTank,
    Tank PublishedTankWithExplicitMainImage,
    Tank DraftTank,
    Comment TankCommentWithAuthor,
    Species CyphotilapiaFrontosa,
    Species NeolamprologusBrichardiDaffodil,
    DiscussionThread CommunityCichlidsThreadA,
    DiscussionThread CommunityCichlidsThreadB,
    DiscussionThread CommunityAfricanThreadA,
    DiscussionThread CommunityMarketPlaceThreadWithNoPosts,
    DiscussionPost CommunityCichlidsThreadAMemberPost,
    DiscussionPost CommunityCichlidsThreadAGuestPost,
    long CommunityCichlidsThreadAGuestPostAttachmentMediaItemId,
    DiscussionPost CommunityCichlidsThreadBPlaceholderPost,
    DiscussionPost CommunityAfricanThreadAPost)
{
    public static async Task<SeedData> CreateAsync(CichlidsDbContext context)
    {
        var now = DateTimeOffset.UtcNow;

        var alice = new Profile
        {
            Username = "alice", DisplayName = "Alice A.", Kind = ProfileKind.Member,
            City = "Berlin", CountryCode = "DE", CreatedAt = now.AddYears(-2), LegacyId = 3001,
        };
        var bob = new Profile
        {
            Username = "bob", DisplayName = "Bob B.", Kind = ProfileKind.Member,
            ExternalAvatarUrl = "https://example.test/avatars/bob.png", CreatedAt = now.AddYears(-1),
        };
        var archivedProfile = new Profile
        {
            Username = "legacy_ghost", DisplayName = "Legacy Ghost", Kind = ProfileKind.Archived, CreatedAt = now.AddYears(-5),
        };
        context.Profiles.AddRange(alice, bob, archivedProfile);
        await context.SaveChangesAsync();

        var aliceAvatarMedia = new MediaItem
        {
            Kind = MediaKind.Photo, StorageKey = "originals/avatars/alice.jpg", CreatedAt = now,
        };
        var bobProfileImageMedia = new MediaItem
        {
            Kind = MediaKind.Photo, StorageKey = "originals/profiles/bob-cover.jpg", CreatedAt = now,
        };
        context.MediaItems.AddRange(aliceAvatarMedia, bobProfileImageMedia);
        await context.SaveChangesAsync();

        context.MediaVariants.Add(new MediaVariant
        {
            MediaItemId = aliceAvatarMedia.Id, Label = "thumb", Width = 200, StorageKey = "variants/thumb/avatars/alice.jpg",
        });
        await context.SaveChangesAsync();

        alice.AvatarMediaId = aliceAvatarMedia.Id;
        bob.ProfileImageMediaId = bobProfileImageMedia.Id;
        await context.SaveChangesAsync();

        var tropheus = new Species
        {
            Genus = "Tropheus", Name = "duboisi", DisplayName = "Tropheus duboisi", Slug = "tropheus-duboisi",
            Category = "Tanganyika cichlid",
            TemperatureRange = "24-27C", PhRange = "7.8-9.0", GhRange = "10-20", KhRange = "10-15", MaxSize = "12cm",
            Breeding = SpeciesBreeding.Mouthbreeder, Aggression = AggressionLevel.Moderate, IntraAggression = AggressionLevel.High,
            Diet = SpeciesDiet.Herbivore, Morphs = "Maswa, Bemba",
            Description = "A rock-dwelling Tanganyika cichlid.", Origin = "Lake Tanganyika", Habitat = "Rocky shore",
            CreatedAt = now,
        };
        var neolamprologus = new Species
        {
            Genus = "Neolamprologus", Name = "pulcher", DisplayName = "Neolamprologus pulcher", Slug = "neolamprologus-pulcher",
            Breeding = SpeciesBreeding.CaveBreeder, Aggression = AggressionLevel.Low, IntraAggression = AggressionLevel.Moderate,
            Diet = SpeciesDiet.Omnivore, CreatedAt = now,
        };
        // Display name intentionally does not equal "{Genus} {Name}", so a wiki redirect lookup
        // for "Cyphotilapia frontosa" only resolves through the genus+name fallback, not the
        // exact display-name match tried first.
        var cyphotilapiaFrontosa = new Species
        {
            Genus = "Cyphotilapia", Name = "frontosa", DisplayName = "Frontosa Cichlid", Slug = "cyphotilapia-frontosa",
            CreatedAt = now,
        };
        // Neither the display name nor "{Genus} {Name}" equals the wiki name below, so a lookup
        // for "Neolamprologus brichardi daffodil" only resolves through the slug fallback.
        var neolamprologusBrichardiDaffodil = new Species
        {
            Genus = "Neolamprologus", Name = "brichardi", DisplayName = "Neolamprologus brichardi (Daffodil)",
            Slug = "neolamprologus-brichardi-daffodil", CreatedAt = now,
        };
        context.Species.AddRange(tropheus, neolamprologus, cyphotilapiaFrontosa, neolamprologusBrichardiDaffodil);
        await context.SaveChangesAsync();

        context.SpeciesCommonNames.AddRange(
            new SpeciesCommonName { SpeciesId = tropheus.Id, Name = "Duboisi cichlid" },
            new SpeciesCommonName { SpeciesId = tropheus.Id, Name = "White spotted cichlid" });
        context.SpeciesLinks.Add(new SpeciesLink
        {
            SpeciesId = tropheus.Id, Url = "https://example.test/tropheus", Label = "Care sheet", Sort = 0,
        });
        await context.SaveChangesAsync();

        var pictureAMedia = new MediaItem { Kind = MediaKind.Photo, StorageKey = "originals/pictures/picture-a.jpg", CreatedAt = now };
        var pictureBMedia = new MediaItem { Kind = MediaKind.Photo, StorageKey = "originals/pictures/picture-b.jpg", CreatedAt = now };
        context.MediaItems.AddRange(pictureAMedia, pictureBMedia);
        await context.SaveChangesAsync();

        context.MediaVariants.AddRange(
            new MediaVariant { MediaItemId = pictureAMedia.Id, Label = "thumb", Width = 200, StorageKey = "variants/thumb/pictures/picture-a.jpg" },
            new MediaVariant { MediaItemId = pictureAMedia.Id, Label = "small", Width = 400, StorageKey = "variants/small/pictures/picture-a.jpg" },
            new MediaVariant { MediaItemId = pictureAMedia.Id, Label = "medium", Width = 800, StorageKey = "variants/medium/pictures/picture-a.jpg" },
            new MediaVariant { MediaItemId = pictureBMedia.Id, Label = "thumb", Width = 200, StorageKey = "variants/thumb/pictures/picture-b.jpg" });
        await context.SaveChangesAsync();

        var pictureA = new Post
        {
            AuthorProfileId = alice.Id, Kind = PostKind.Single, Topic = PostTopic.Cichlids,
            Title = "Reef Tank Beauty", Description = "A colorful cichlid", State = PostState.Published,
            ViewCount = 10, RatingAverage = 4.5, RatingCount = 20, CreatedAt = now.AddDays(-10), PublishedAt = now.AddDays(-1),
        };
        var pictureB = new Post
        {
            AuthorProfileId = bob.Id, Kind = PostKind.Single, Topic = PostTopic.Tanks,
            Title = "Tank Story", State = PostState.Published,
            ViewCount = 100, RatingAverage = 3.0, RatingCount = 5, CreatedAt = now.AddDays(-20), PublishedAt = now.AddDays(-2),
        };
        var pictureC = new Post
        {
            AuthorProfileId = alice.Id, Kind = PostKind.Single, Topic = PostTopic.Cichlids,
            Title = "High Score Newcomer", State = PostState.Published,
            ViewCount = 0, RatingAverage = 5.0, RatingCount = 1, CreatedAt = now.AddHours(-13), PublishedAt = now.AddHours(-12),
        };
        var offtopicPicture = new Post
        {
            AuthorProfileId = alice.Id, Kind = PostKind.Single, Topic = PostTopic.Offtopic,
            Title = "Off Topic Chat", State = PostState.Published, CreatedAt = now.AddDays(-30), PublishedAt = now.AddDays(-3),
        };
        var draftPicture = new Post
        {
            AuthorProfileId = alice.Id, Kind = PostKind.Single, Topic = PostTopic.Cichlids,
            Title = "Unpublished Draft", State = PostState.Draft, CreatedAt = now.AddDays(-1),
        };
        var archivedPicture = new Post
        {
            AuthorProfileId = alice.Id, Kind = PostKind.Single, Topic = PostTopic.Cichlids,
            Title = "Old Archived Post", State = PostState.Archived, CreatedAt = now.AddDays(-100), PublishedAt = now.AddDays(-90),
        };
        var deletedPicture = new Post
        {
            AuthorProfileId = alice.Id, Kind = PostKind.Single, Topic = PostTopic.Cichlids,
            Title = "Removed Post", State = PostState.Published, CreatedAt = now.AddDays(-5), PublishedAt = now.AddDays(-4),
            DeletedAt = now.AddDays(-1), DeleteReason = "test cleanup",
        };
        context.Posts.AddRange(pictureA, pictureB, pictureC, offtopicPicture, draftPicture, archivedPicture, deletedPicture);
        await context.SaveChangesAsync();

        context.PostMedia.AddRange(
            new PostMedia { PostId = pictureA.Id, MediaItemId = pictureAMedia.Id, Sort = 0 },
            new PostMedia { PostId = pictureB.Id, MediaItemId = pictureBMedia.Id, Sort = 0 });
        await context.SaveChangesAsync();

        // pictureA and pictureC (both published) depict tropheus, so its public picture count is
        // 2; draftPicture also depicts tropheus but must not count since it is never public.
        // pictureB depicts neolamprologus, keeping the two species filters distinguishable.
        context.PostSpecies.AddRange(
            new PostSpecies { PostId = pictureA.Id, SpeciesId = tropheus.Id, Sort = 0 },
            new PostSpecies { PostId = pictureC.Id, SpeciesId = tropheus.Id, Sort = 0 },
            new PostSpecies { PostId = draftPicture.Id, SpeciesId = tropheus.Id, Sort = 0 },
            new PostSpecies { PostId = pictureB.Id, SpeciesId = neolamprologus.Id, Sort = 0 });
        await context.SaveChangesAsync();

        const string canonicalSlugA = "reef-tank-beauty";
        const string altSlugA = "old-reef-tank-beauty";
        const string canonicalSlugB = "tank-story";
        const string canonicalSlugC = "high-score-newcomer";
        const string offtopicSlug = "offtopic-post";
        const string draftSlug = "draft-post";
        const string archivedSlug = "archived-post";
        const string deletedSlug = "deleted-post";

        context.SlugAliases.AddRange(
            new SlugAlias { PostId = pictureA.Id, Value = canonicalSlugA, IsCanonical = true, CreatedAt = now },
            new SlugAlias { PostId = pictureA.Id, Value = altSlugA, IsCanonical = false, CreatedAt = now.AddDays(-9) },
            new SlugAlias { PostId = pictureB.Id, Value = canonicalSlugB, IsCanonical = true, CreatedAt = now },
            new SlugAlias { PostId = pictureC.Id, Value = canonicalSlugC, IsCanonical = true, CreatedAt = now },
            new SlugAlias { PostId = offtopicPicture.Id, Value = offtopicSlug, IsCanonical = true, CreatedAt = now },
            new SlugAlias { PostId = draftPicture.Id, Value = draftSlug, IsCanonical = true, CreatedAt = now },
            new SlugAlias { PostId = archivedPicture.Id, Value = archivedSlug, IsCanonical = true, CreatedAt = now },
            new SlugAlias { PostId = deletedPicture.Id, Value = deletedSlug, IsCanonical = true, CreatedAt = now });
        await context.SaveChangesAsync();

        var pictureCommentWithAuthor = new Comment
        {
            PostId = pictureA.Id, AuthorProfileId = bob.Id, Body = "Great fish!", Score = 2, CreatedAt = now.AddHours(-2),
        };
        var pictureCommentAnonymous = new Comment
        {
            PostId = pictureA.Id, PosterName = "Guest123", Body = "Nice!", Score = 0, CreatedAt = now.AddHours(-1),
        };
        var pictureCommentDeleted = new Comment
        {
            PostId = pictureA.Id, AuthorProfileId = alice.Id, Body = "oops", Score = 0, CreatedAt = now.AddMinutes(-30),
            DeletedAt = now, DeleteReason = "retracted",
        };
        context.Comments.AddRange(pictureCommentWithAuthor, pictureCommentAnonymous, pictureCommentDeleted);
        await context.SaveChangesAsync();

        var tankShowcase1Media = new MediaItem { Kind = MediaKind.Photo, StorageKey = "originals/tanks/showcase-1.jpg", CreatedAt = now };
        var tankShowcase2Media = new MediaItem { Kind = MediaKind.Photo, StorageKey = "originals/tanks/showcase-2.jpg", CreatedAt = now };
        var tankDecorationMedia = new MediaItem { Kind = MediaKind.Photo, StorageKey = "originals/tanks/decoration-1.jpg", CreatedAt = now };
        var tank2MainMedia = new MediaItem { Kind = MediaKind.Photo, StorageKey = "originals/tanks/malawi-main.jpg", CreatedAt = now };
        context.MediaItems.AddRange(tankShowcase1Media, tankShowcase2Media, tankDecorationMedia, tank2MainMedia);
        await context.SaveChangesAsync();

        context.MediaVariants.AddRange(
            new MediaVariant { MediaItemId = tankShowcase1Media.Id, Label = "thumb", Width = 200, StorageKey = "variants/thumb/tanks/showcase-1.jpg" },
            new MediaVariant { MediaItemId = tankShowcase2Media.Id, Label = "thumb", Width = 200, StorageKey = "variants/thumb/tanks/showcase-2.jpg" },
            new MediaVariant { MediaItemId = tankDecorationMedia.Id, Label = "thumb", Width = 200, StorageKey = "variants/thumb/tanks/decoration-1.jpg" },
            new MediaVariant { MediaItemId = tank2MainMedia.Id, Label = "thumb", Width = 200, StorageKey = "variants/thumb/tanks/malawi-main.jpg" });
        await context.SaveChangesAsync();

        var publishedTank = new Tank
        {
            ProfileId = bob.Id, Title = "Tanganyika 240L", Category = TankCategory.Tanganyika,
            WidthValue = 120, HeightValue = 50, DepthValue = 50, DimensionUnit = DimensionUnit.Cm,
            Gravel = "Sand", Plants = "Anubias", Decoration = "Rocks", Light = "LED", LightDuration = "10h",
            Filtration = "Canister", Technic = "Heater 200W",
            WaterPh = "8.2", WaterKh = "12", WaterGh = "15", WaterNo2 = "0", WaterNo3 = "10", WaterPo4 = "0.5",
            WaterNotes = "Weekly 30% change", Food = "Pellets", Notes = "Established 2023",
            State = TankState.Published, CreatedAt = now.AddDays(-40), PublishedAt = now.AddDays(-5), LegacyId = 5001,
        };
        var publishedTankWithExplicitMainImage = new Tank
        {
            ProfileId = alice.Id, Title = "Malawi Community", Category = TankCategory.Malawi,
            State = TankState.Published, CreatedAt = now.AddDays(-50), PublishedAt = now.AddDays(-6),
        };
        var draftTank = new Tank
        {
            ProfileId = bob.Id, Title = "Unfinished Tank", Category = TankCategory.Tanganyika,
            State = TankState.Draft, CreatedAt = now.AddDays(-2),
        };
        context.Tanks.AddRange(publishedTank, publishedTankWithExplicitMainImage, draftTank);
        await context.SaveChangesAsync();

        publishedTankWithExplicitMainImage.MainMediaId = tank2MainMedia.Id;
        await context.SaveChangesAsync();

        context.TankMedia.AddRange(
            new TankMedia { TankId = publishedTank.Id, MediaItemId = tankShowcase1Media.Id, Section = TankMediaSection.Showcase, Sort = 0 },
            new TankMedia { TankId = publishedTank.Id, MediaItemId = tankShowcase2Media.Id, Section = TankMediaSection.Showcase, Sort = 1 },
            new TankMedia { TankId = publishedTank.Id, MediaItemId = tankDecorationMedia.Id, Section = TankMediaSection.Decoration, Sort = 0 });
        await context.SaveChangesAsync();

        context.Inhabitants.AddRange(
            new Inhabitant { TankId = publishedTank.Id, SpeciesId = tropheus.Id, Count = 6, Sort = 0 },
            new Inhabitant { TankId = publishedTank.Id, SpeciesId = neolamprologus.Id, Count = 8, Sort = 1 });
        await context.SaveChangesAsync();

        var tankCommentWithAuthor = new Comment
        {
            TankId = publishedTank.Id, AuthorProfileId = alice.Id, Body = "Beautiful setup!", Score = 1, CreatedAt = now.AddHours(-3),
        };
        var tankCommentDeleted = new Comment
        {
            TankId = publishedTank.Id, AuthorProfileId = bob.Id, Body = "spam", Score = 0, CreatedAt = now.AddHours(-4),
            DeletedAt = now, DeleteReason = "spam",
        };
        context.Comments.AddRange(tankCommentWithAuthor, tankCommentDeleted);
        await context.SaveChangesAsync();

        var forumAttachmentMedia = new MediaItem { Kind = MediaKind.Photo, StorageKey = "originals/forum/attachment-1.jpg", CreatedAt = now };
        context.MediaItems.Add(forumAttachmentMedia);
        await context.SaveChangesAsync();

        context.MediaVariants.AddRange(
            new MediaVariant { MediaItemId = forumAttachmentMedia.Id, Label = "thumb", Width = 200, StorageKey = "variants/thumb/forum/attachment-1.jpg" },
            new MediaVariant { MediaItemId = forumAttachmentMedia.Id, Label = "small", Width = 400, StorageKey = "variants/small/forum/attachment-1.jpg" });
        await context.SaveChangesAsync();

        // Thread A: a real member opens the thread, a guest without a profile replies with an
        // attachment. Its legacy id matches the read.php redirect smoke value.
        var communityCichlidsThreadA = new DiscussionThread
        {
            LegacyId = 9001, Category = DiscussionCategory.Cichlids, Title = "Best filtration setup for a 200L tank",
            State = DiscussionThreadState.Archived, CreatedAt = now.AddDays(-10), LastPostAt = now.AddDays(-8), PostCount = 2,
        };
        // Thread B: its only post is attributed to the unlisted placeholder profile created for a
        // forum author with no matching member account (ADR-0021). Posted more recently than
        // thread A, so the default last-post-at ordering places it first.
        var communityCichlidsThreadB = new DiscussionThread
        {
            Category = DiscussionCategory.Cichlids, Title = "pH swings after water change",
            State = DiscussionThreadState.Archived, CreatedAt = now.AddDays(-5), LastPostAt = now.AddDays(-5), PostCount = 1,
        };
        var communityAfricanThreadA = new DiscussionThread
        {
            Category = DiscussionCategory.African, Title = "Malawi vs Tanganyika biotope debate",
            State = DiscussionThreadState.Archived, CreatedAt = now.AddDays(-6), LastPostAt = now.AddDays(-6), PostCount = 1,
        };
        // A thread with no posts and a null last-post-at, a shape the schema allows although
        // ForumMigrationStep never produces it for a migrated Phorum root; created most recently of
        // all four so a naive "most recent first" ordering would put it first, but it must sort
        // after every thread that has a post.
        var communityMarketPlaceThreadWithNoPosts = new DiscussionThread
        {
            Category = DiscussionCategory.MarketPlace, Title = "Empty thread with no posts",
            State = DiscussionThreadState.Archived, CreatedAt = now.AddDays(-1), LastPostAt = null, PostCount = 0,
        };
        context.DiscussionThreads.AddRange(
            communityCichlidsThreadA, communityCichlidsThreadB, communityAfricanThreadA, communityMarketPlaceThreadWithNoPosts);
        await context.SaveChangesAsync();

        var communityCichlidsThreadAMemberPost = new DiscussionPost
        {
            ThreadId = communityCichlidsThreadA.Id, AuthorProfileId = alice.Id,
            Body = "I run a canister filter rated for twice the tank volume, works well.",
            CreatedAt = now.AddDays(-10), Sort = 0,
        };
        var communityCichlidsThreadAGuestPost = new DiscussionPost
        {
            ThreadId = communityCichlidsThreadA.Id, PosterName = "Guest Fisher",
            Body = "Here is a picture of my own setup for comparison.",
            CreatedAt = now.AddDays(-8), Sort = 1,
        };
        var communityCichlidsThreadBPlaceholderPost = new DiscussionPost
        {
            ThreadId = communityCichlidsThreadB.Id, AuthorProfileId = archivedProfile.Id,
            Body = "Check your KH, a swing usually means it bottomed out.",
            CreatedAt = now.AddDays(-5), Sort = 0,
        };
        var communityAfricanThreadAPost = new DiscussionPost
        {
            ThreadId = communityAfricanThreadA.Id, AuthorProfileId = bob.Id,
            Body = "Both are great, depends how much aggression you want to manage.",
            CreatedAt = now.AddDays(-6), Sort = 0,
        };
        context.DiscussionPosts.AddRange(
            communityCichlidsThreadAMemberPost, communityCichlidsThreadAGuestPost,
            communityCichlidsThreadBPlaceholderPost, communityAfricanThreadAPost);
        await context.SaveChangesAsync();

        context.DiscussionPostMedia.Add(new DiscussionPostMedia
        {
            DiscussionPostId = communityCichlidsThreadAGuestPost.Id, MediaItemId = forumAttachmentMedia.Id, Sort = 0,
        });
        await context.SaveChangesAsync();

        return new SeedData(
            alice, bob, archivedProfile,
            tropheus, neolamprologus,
            pictureA, canonicalSlugA, altSlugA,
            pictureB, canonicalSlugB,
            pictureC, canonicalSlugC,
            offtopicPicture, offtopicSlug,
            draftPicture, draftSlug,
            archivedPicture, archivedSlug,
            deletedPicture, deletedSlug,
            pictureCommentWithAuthor, pictureCommentAnonymous,
            publishedTank, publishedTankWithExplicitMainImage, draftTank,
            tankCommentWithAuthor,
            cyphotilapiaFrontosa, neolamprologusBrichardiDaffodil,
            communityCichlidsThreadA, communityCichlidsThreadB, communityAfricanThreadA, communityMarketPlaceThreadWithNoPosts,
            communityCichlidsThreadAMemberPost, communityCichlidsThreadAGuestPost, forumAttachmentMedia.Id,
            communityCichlidsThreadBPlaceholderPost, communityAfricanThreadAPost);
    }
}
