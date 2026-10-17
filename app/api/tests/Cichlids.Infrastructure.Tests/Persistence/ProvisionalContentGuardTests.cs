using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Cichlids.Infrastructure.Tests.Persistence;

/// <summary>
/// The database refuses content of a provisional profile that would reach other people, whichever
/// statement writes it: tanks with a visibility other than private and rows of the gated actions
/// (follows, comments, ratings, kudos, stories, selected members, profile media, forum posts and
/// collections). The refusals use the SQLSTATE PV001. Deletes and writes for member profiles pass.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Story", "TASK-7.6")]
public sealed class ProvisionalContentGuardTests(PostgresFixture fixture)
{
    private const string GuardSqlState = "PV001";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public static TheoryData<string> GatedProfileTables => new()
    {
        "follow of a profile by the provisional profile",
        "follow of the provisional profile",
        "follow of a tank by the provisional profile",
        "comment on a tank",
        "comment on a story",
        "rating of a tank",
        "rating of a story",
        "comment vote",
        "selected member",
        "story",
        "discussion post",
        "collection",
        "profile media",
    };

    public static TheoryData<string> GatedTankTables => new()
    {
        "follow of the tank",
        "comment on the tank",
        "rating of the tank",
        "selected member of the tank",
        "story subject of the tank",
    };

    public static TheoryData<TankVisibility, TankVisibility> WiderVisibilities => new()
    {
        { TankVisibility.Public, TankVisibility.Private },
        { TankVisibility.Selected, TankVisibility.Private },
        { TankVisibility.Followers, TankVisibility.Selected },
        { TankVisibility.Public, TankVisibility.Public },
    };

    [Theory]
    [MemberData(nameof(WiderVisibilities))]
    public async Task GivenAProvisionalProfile_WhenATankIsInsertedWithAWiderVisibility_ThenTheDatabaseRefusesIt(
        TankVisibility profileVisibility, TankVisibility streamVisibility)
    {
        await using var context = fixture.CreateContext();
        var provisional = await NewProfileAsync(context, ProfileKind.Provisional);
        context.Tanks.Add(NewTank(provisional.Id, profileVisibility, streamVisibility));

        await AssertRefusedAsync(() => context.SaveChangesAsync());
    }

    [Theory]
    [MemberData(nameof(WiderVisibilities))]
    public async Task GivenAPrivateTankOfAProvisionalProfile_WhenItsVisibilityIsWidenedPastTheApi_ThenTheDatabaseRefusesIt(
        TankVisibility profileVisibility, TankVisibility streamVisibility)
    {
        await using var context = fixture.CreateContext();
        var provisional = await NewProfileAsync(context, ProfileKind.Provisional);
        var tank = NewTank(provisional.Id, TankVisibility.Private, TankVisibility.Private);
        context.Tanks.Add(tank);
        await context.SaveChangesAsync();

        var profile = profileVisibility.ToString().ToLowerInvariant();
        var stream = streamVisibility.ToString().ToLowerInvariant();
        await AssertRefusedAsync(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.tank SET profile_visibility = {profile}, stream_visibility = {stream} WHERE id = {tank.Id}"));
        Assert.Equal(TankVisibility.Private, (await context.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank.Id)).ProfileVisibility);
    }

    [Fact]
    public async Task GivenAPublicTankOfAMember_WhenItMovesToAProvisionalProfile_ThenTheDatabaseRefusesIt()
    {
        await using var context = fixture.CreateContext();
        var member = await NewProfileAsync(context, ProfileKind.Member);
        var provisional = await NewProfileAsync(context, ProfileKind.Provisional);
        var tank = NewTank(member.Id, TankVisibility.Public, TankVisibility.Public);
        context.Tanks.Add(tank);
        await context.SaveChangesAsync();

        await AssertRefusedAsync(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.tank SET profile_id = {provisional.Id} WHERE id = {tank.Id}"));
    }

    [Fact]
    public async Task GivenAPrivateTankOfAMember_WhenItMovesToAProvisionalProfile_ThenTheMoveIsAllowed()
    {
        await using var context = fixture.CreateContext();
        var member = await NewProfileAsync(context, ProfileKind.Member);
        var provisional = await NewProfileAsync(context, ProfileKind.Provisional);
        var tank = NewTank(member.Id, TankVisibility.Private, TankVisibility.Private);
        context.Tanks.Add(tank);
        await context.SaveChangesAsync();

        var rows = await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.tank SET profile_id = {provisional.Id} WHERE id = {tank.Id}");

        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task GivenAProvisionalProfile_WhenItsPrivateTankIsChangedOtherwise_ThenTheWriteIsAllowed()
    {
        await using var context = fixture.CreateContext();
        var provisional = await NewProfileAsync(context, ProfileKind.Provisional);
        var tank = NewTank(provisional.Id, TankVisibility.Private, TankVisibility.Private);
        context.Tanks.Add(tank);
        await context.SaveChangesAsync();

        tank.Title = "Renamed";
        await context.SaveChangesAsync();

        Assert.Equal("Renamed", (await context.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank.Id)).Title);
    }

    [Fact]
    public async Task GivenAMember_WhenItWritesWidePublicTanks_ThenTheWritesAreAllowed()
    {
        await using var context = fixture.CreateContext();
        var member = await NewProfileAsync(context, ProfileKind.Member);
        var tank = NewTank(member.Id, TankVisibility.Public, TankVisibility.Followers);
        context.Tanks.Add(tank);
        await context.SaveChangesAsync();

        var rows = await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.tank SET profile_visibility = 'followers', stream_visibility = 'selected' WHERE id = {tank.Id}");

        Assert.Equal(1, rows);
    }

    [Theory]
    [MemberData(nameof(GatedProfileTables))]
    public async Task GivenAProvisionalProfile_WhenARowOfAGatedActionIsInserted_ThenTheDatabaseRefusesIt(string kind)
    {
        await using var context = fixture.CreateContext();
        var scene = await NewSceneAsync(context);

        await AssertRefusedAsync(() => InsertForProvisionalAsync(context, scene, kind));
    }

    [Theory]
    [MemberData(nameof(GatedProfileTables))]
    public async Task GivenAMember_WhenARowOfAGatedActionIsInserted_ThenTheWriteIsAllowed(string kind)
    {
        await using var context = fixture.CreateContext();
        var scene = await NewSceneAsync(context);

        await InsertForMemberAsync(context, scene, kind);
    }

    [Theory]
    [MemberData(nameof(GatedTankTables))]
    public async Task GivenATankOfAProvisionalProfile_WhenARowThatReachesOtherPeopleReferencesIt_ThenTheDatabaseRefusesIt(string kind)
    {
        await using var context = fixture.CreateContext();
        var scene = await NewSceneAsync(context);

        await AssertRefusedAsync(() => InsertForProvisionalTankAsync(context, scene, kind));
    }

    [Fact]
    public async Task GivenARowOfAMember_WhenItsOwnerBecomesTheProvisionalProfile_ThenTheDatabaseRefusesIt()
    {
        await using var context = fixture.CreateContext();
        var scene = await NewSceneAsync(context);
        context.Comments.Add(new Comment { TankId = scene.MemberTank.Id, AuthorProfileId = scene.Member.Id, Body = "Nice", CreatedAt = Now });
        await context.SaveChangesAsync();

        await AssertRefusedAsync(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.comment SET author_profile_id = {scene.Provisional.Id} WHERE author_profile_id = {scene.Member.Id}"));
    }

    [Fact]
    public async Task GivenAProvisionalProfile_WhenItBecomesAMember_ThenItsTanksAndRowsAreWritableAsThoseOfAMember()
    {
        await using var context = fixture.CreateContext();
        var scene = await NewSceneAsync(context);
        var tank = NewTank(scene.Provisional.Id, TankVisibility.Private, TankVisibility.Private);
        context.Tanks.Add(tank);
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.profile SET kind = 'member' WHERE id = {scene.Provisional.Id}");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.tank SET profile_visibility = 'public', stream_visibility = 'public' WHERE id = {tank.Id}");
        context.Follows.Add(new Follow { FollowerProfileId = scene.Provisional.Id, TargetTankId = scene.MemberTank.Id, CreatedAt = Now });
        await context.SaveChangesAsync();

        Assert.Equal(TankVisibility.Public, (await context.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank.Id)).ProfileVisibility);
    }

    [Fact]
    public async Task GivenAProvisionalProfileWithATank_WhenItsTankMovesIntoAMemberProfile_ThenTheMoveAndTheRemovalPass()
    {
        await using var context = fixture.CreateContext();
        var scene = await NewSceneAsync(context);
        var tank = NewTank(scene.Provisional.Id, TankVisibility.Private, TankVisibility.Private);
        context.Tanks.Add(tank);
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.tank SET profile_id = {scene.Member.Id} WHERE profile_id = {scene.Provisional.Id}");
        var removed = await context.Profiles.Where(p => p.Id == scene.Provisional.Id).ExecuteDeleteAsync();

        Assert.Equal(1, removed);
        Assert.Equal(scene.Member.Id, (await context.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank.Id)).ProfileId);
    }

    [Fact]
    public async Task GivenAProvisionalProfileWithATank_WhenItIsDeleted_ThenTheTankGoesWithIt()
    {
        await using var context = fixture.CreateContext();
        var scene = await NewSceneAsync(context);
        var tank = NewTank(scene.Provisional.Id, TankVisibility.Private, TankVisibility.Private);
        context.Tanks.Add(tank);
        await context.SaveChangesAsync();

        await context.Tanks.Where(t => t.Id == tank.Id).ExecuteDeleteAsync();
        var removed = await context.Profiles.Where(p => p.Id == scene.Provisional.Id).ExecuteDeleteAsync();

        Assert.Equal(1, removed);
    }

    [Fact]
    public async Task GivenTheGuardMigration_WhenItIsRevertedAndAppliedAgain_ThenItsFunctionsAndTriggersAreDroppedAndCreated()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync();
        const string guardObjects = """
            SELECT ((SELECT count(*) FROM pg_proc WHERE proname IN ('profile_is_provisional', 'tank_of_provisional_profile_must_be_private',
                        'reject_provisional_profile_rows', 'reject_rows_of_provisional_tanks'))
                 + (SELECT count(*) FROM pg_trigger WHERE tgname IN ('tank_of_provisional_profile_must_be_private',
                        'provisional_content_guard', 'provisional_tank_guard')))::int AS "Value"
            """;
        const int expected = 4 + 1 + 9 + 5;
        Assert.Equal(expected, await context.Database.SqlQueryRaw<int>(guardObjects).SingleAsync());

        await migrator.MigrateAsync("20261016203147_DeviceCredentials");
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>(guardObjects).SingleAsync());

        await migrator.MigrateAsync();
        Assert.Equal(expected, await context.Database.SqlQueryRaw<int>(guardObjects).SingleAsync());
    }

    private static async Task InsertForProvisionalAsync(CichlidsDbContext context, Scene scene, string kind)
    {
        var provisional = scene.Provisional.Id;
        var member = scene.Member.Id;
        switch (kind)
        {
            case "follow of a profile by the provisional profile":
                context.Follows.Add(new Follow { FollowerProfileId = provisional, TargetProfileId = member, CreatedAt = Now });
                break;
            case "follow of the provisional profile":
                context.Follows.Add(new Follow { FollowerProfileId = member, TargetProfileId = provisional, CreatedAt = Now });
                break;
            case "follow of a tank by the provisional profile":
                context.Follows.Add(new Follow { FollowerProfileId = provisional, TargetTankId = scene.MemberTank.Id, CreatedAt = Now });
                break;
            case "comment on a tank":
                context.Comments.Add(new Comment { TankId = scene.MemberTank.Id, AuthorProfileId = provisional, Body = "Nice", CreatedAt = Now });
                break;
            case "comment on a story":
                context.Comments.Add(new Comment { StoryId = scene.MemberStory.Id, AuthorProfileId = provisional, Body = "Nice", CreatedAt = Now });
                break;
            case "rating of a tank":
                context.Ratings.Add(new Rating { TankId = scene.MemberTank.Id, ProfileId = provisional, Stars = 5, CreatedAt = Now });
                break;
            case "rating of a story":
                context.Ratings.Add(new Rating { StoryId = scene.MemberStory.Id, ProfileId = provisional, Stars = 5, CreatedAt = Now });
                break;
            case "comment vote":
                context.CommentVotes.Add(new CommentVote { CommentId = scene.MemberComment.Id, ProfileId = provisional, Value = 1, CreatedAt = Now });
                break;
            case "selected member":
                context.TankSelectedMembers.Add(new TankSelectedMember { TankId = scene.MemberTank.Id, MemberProfileId = provisional, AddedAt = Now });
                break;
            case "story":
                context.Stories.Add(new Story { AuthorProfileId = provisional, Topic = StoryTopic.Cichlids, State = StoryState.Draft, CreatedAt = Now });
                break;
            case "discussion post":
                context.DiscussionPosts.Add(new DiscussionPost { ThreadId = scene.Thread.Id, AuthorProfileId = provisional, Body = "Hello", CreatedAt = Now });
                break;
            case "collection":
                context.Collections.Add(new Collection { ProfileId = provisional, Title = "Mine", CreatedAt = Now });
                break;
            case "profile media":
                context.MediaLinks.Add(MediaLink.ForProfile(provisional, scene.Media.Id, MediaLinkRole.Avatar));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        await context.SaveChangesAsync();
    }

    private static async Task InsertForMemberAsync(CichlidsDbContext context, Scene scene, string kind)
    {
        var other = scene.OtherMember.Id;
        var member = scene.Member.Id;
        switch (kind)
        {
            case "follow of a profile by the provisional profile":
                context.Follows.Add(new Follow { FollowerProfileId = other, TargetProfileId = member, CreatedAt = Now });
                break;
            case "follow of the provisional profile":
                context.Follows.Add(new Follow { FollowerProfileId = member, TargetProfileId = other, CreatedAt = Now });
                break;
            case "follow of a tank by the provisional profile":
                context.Follows.Add(new Follow { FollowerProfileId = other, TargetTankId = scene.MemberTank.Id, CreatedAt = Now });
                break;
            case "comment on a tank":
                context.Comments.Add(new Comment { TankId = scene.MemberTank.Id, AuthorProfileId = other, Body = "Nice", CreatedAt = Now });
                break;
            case "comment on a story":
                context.Comments.Add(new Comment { StoryId = scene.MemberStory.Id, AuthorProfileId = other, Body = "Nice", CreatedAt = Now });
                break;
            case "rating of a tank":
                context.Ratings.Add(new Rating { TankId = scene.MemberTank.Id, ProfileId = other, Stars = 5, CreatedAt = Now });
                break;
            case "rating of a story":
                context.Ratings.Add(new Rating { StoryId = scene.MemberStory.Id, ProfileId = other, Stars = 5, CreatedAt = Now });
                break;
            case "comment vote":
                context.CommentVotes.Add(new CommentVote { CommentId = scene.MemberComment.Id, ProfileId = other, Value = 1, CreatedAt = Now });
                break;
            case "selected member":
                context.TankSelectedMembers.Add(new TankSelectedMember { TankId = scene.MemberTank.Id, MemberProfileId = other, AddedAt = Now });
                break;
            case "story":
                context.Stories.Add(new Story { AuthorProfileId = other, Topic = StoryTopic.Cichlids, State = StoryState.Draft, CreatedAt = Now });
                break;
            case "discussion post":
                context.DiscussionPosts.Add(new DiscussionPost { ThreadId = scene.Thread.Id, AuthorProfileId = other, Body = "Hello", CreatedAt = Now });
                break;
            case "collection":
                context.Collections.Add(new Collection { ProfileId = other, Title = "Mine", CreatedAt = Now });
                break;
            case "profile media":
                context.MediaLinks.Add(MediaLink.ForProfile(other, scene.Media.Id, MediaLinkRole.Avatar));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        await context.SaveChangesAsync();
    }

    private static async Task InsertForProvisionalTankAsync(CichlidsDbContext context, Scene scene, string kind)
    {
        var tank = NewTank(scene.Provisional.Id, TankVisibility.Private, TankVisibility.Private);
        context.Tanks.Add(tank);
        await context.SaveChangesAsync();
        switch (kind)
        {
            case "follow of the tank":
                context.Follows.Add(new Follow { FollowerProfileId = scene.Member.Id, TargetTankId = tank.Id, CreatedAt = Now });
                break;
            case "comment on the tank":
                context.Comments.Add(new Comment { TankId = tank.Id, AuthorProfileId = scene.Member.Id, Body = "Nice", CreatedAt = Now });
                break;
            case "rating of the tank":
                context.Ratings.Add(new Rating { TankId = tank.Id, ProfileId = scene.Member.Id, Stars = 4, CreatedAt = Now });
                break;
            case "selected member of the tank":
                context.TankSelectedMembers.Add(new TankSelectedMember { TankId = tank.Id, MemberProfileId = scene.Member.Id, AddedAt = Now });
                break;
            case "story subject of the tank":
                var subject = StorySubject.ForTank(tank.Id);
                subject.StoryId = scene.MemberStory.Id;
                context.StorySubjects.Add(subject);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        await context.SaveChangesAsync();
    }

    private static async Task AssertRefusedAsync(Func<Task> write)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(write);
        var postgres = exception as PostgresException ?? exception.InnerException as PostgresException;
        Assert.NotNull(postgres);
        Assert.Equal(GuardSqlState, postgres.SqlState);
    }

    private static Tank NewTank(long profileId, TankVisibility profileVisibility, TankVisibility streamVisibility) => new()
    {
        ProfileId = profileId,
        Title = $"Tank {Guid.NewGuid():N}",
        State = TankState.Draft,
        ProfileVisibility = profileVisibility,
        StreamVisibility = streamVisibility,
        CreatedAt = Now,
    };

    private static async Task<Profile> NewProfileAsync(CichlidsDbContext context, ProfileKind kind)
    {
        var profile = new Profile { Username = $"guard-{Guid.NewGuid():N}", Kind = kind, CreatedAt = Now };
        context.Profiles.Add(profile);
        await context.SaveChangesAsync();
        return profile;
    }

    private static async Task<Scene> NewSceneAsync(CichlidsDbContext context)
    {
        var member = await NewProfileAsync(context, ProfileKind.Member);
        var otherMember = await NewProfileAsync(context, ProfileKind.Member);
        var provisional = await NewProfileAsync(context, ProfileKind.Provisional);
        var tank = NewTank(member.Id, TankVisibility.Public, TankVisibility.Public);
        var story = new Story { AuthorProfileId = member.Id, Topic = StoryTopic.Cichlids, State = StoryState.Draft, CreatedAt = Now };
        var thread = new DiscussionThread { Category = DiscussionCategory.African, Title = "Guard", State = DiscussionThreadState.Archived, CreatedAt = Now };
        var media = new MediaItem { OwnerProfileId = member.Id, Kind = MediaKind.Photo, StorageKey = $"k-{Guid.NewGuid():N}", CreatedAt = Now };
        context.AddRange(tank, story, thread, media);
        await context.SaveChangesAsync();
        var comment = new Comment { TankId = tank.Id, AuthorProfileId = member.Id, Body = "First", CreatedAt = Now };
        context.Comments.Add(comment);
        await context.SaveChangesAsync();
        return new Scene(member, otherMember, provisional, tank, story, thread, media, comment);
    }

    private sealed record Scene(
        Profile Member, Profile OtherMember, Profile Provisional, Tank MemberTank, Story MemberStory,
        DiscussionThread Thread, MediaItem Media, Comment MemberComment);
}
