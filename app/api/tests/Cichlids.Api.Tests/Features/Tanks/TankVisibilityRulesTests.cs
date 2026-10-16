using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Tanks;

public sealed class TankVisibilityRulesTests
{
    private const TankVisibility Public = TankVisibility.Public;
    private const TankVisibility Private = TankVisibility.Private;
    private const TankVisibility Followers = TankVisibility.Followers;
    private const TankVisibility Selected = TankVisibility.Selected;

    [Theory]
    [InlineData(TankViewer.Owner, Public, true)]
    [InlineData(TankViewer.Owner, Private, true)]
    [InlineData(TankViewer.Member, Public, true)]
    [InlineData(TankViewer.Member, Private, false)]
    [InlineData(TankViewer.Anonymous, Public, true)]
    [InlineData(TankViewer.Anonymous, Private, false)]
    public void CanSeeProfile_FollowsTheProfileLevel_AndTheOwnerSeesEverything(TankViewer viewer, TankVisibility profile, bool expected) =>
        Assert.Equal(expected, TankVisibilityRules.CanSeeProfile(viewer, profile));

    [Theory]
    [InlineData(TankViewer.Owner, Public, Public, true)]
    [InlineData(TankViewer.Owner, Public, Private, true)]
    [InlineData(TankViewer.Owner, Private, Private, true)]
    [InlineData(TankViewer.Member, Public, Public, true)]
    [InlineData(TankViewer.Member, Public, Private, false)]
    [InlineData(TankViewer.Member, Private, Private, false)]
    [InlineData(TankViewer.Anonymous, Public, Public, true)]
    [InlineData(TankViewer.Anonymous, Public, Private, false)]
    [InlineData(TankViewer.Anonymous, Private, Private, false)]
    public void CanSeeStream_NeedsAVisibleProfileAndAnOpenStream(TankViewer viewer, TankVisibility profile, TankVisibility stream, bool expected) =>
        Assert.Equal(expected, TankVisibilityRules.CanSeeStream(viewer, profile, stream));

    [Theory]
    [InlineData(TankViewer.Owner, Public, Public, true, true)]
    [InlineData(TankViewer.Owner, Public, Public, false, true)]
    [InlineData(TankViewer.Owner, Private, Private, true, true)]
    [InlineData(TankViewer.Member, Public, Public, false, true)]
    [InlineData(TankViewer.Member, Public, Public, true, false)]
    [InlineData(TankViewer.Anonymous, Public, Public, false, true)]
    [InlineData(TankViewer.Anonymous, Public, Public, true, false)]
    [InlineData(TankViewer.Member, Public, Private, false, false)]
    [InlineData(TankViewer.Member, Private, Private, false, false)]
    public void CanSeeEntry_HidesEntriesMarkedOnlyMeFromEveryoneButTheOwner(
        TankViewer viewer, TankVisibility profile, TankVisibility stream, bool onlyMe, bool expected) =>
        Assert.Equal(expected, TankVisibilityRules.CanSeeEntry(viewer, profile, stream, onlyMe));

    [Theory]
    [InlineData(Public, Public, true)]
    [InlineData(Public, Private, true)]
    [InlineData(Private, Private, true)]
    [InlineData(Private, Public, false)]
    public void IsStreamWithinProfile_RefusesAStreamWiderThanTheProfile(TankVisibility profile, TankVisibility stream, bool expected) =>
        Assert.Equal(expected, TankVisibilityRules.IsStreamWithinProfile(profile, stream));

    [Theory]
    [InlineData(TankViewer.Member, Public, true)]
    [InlineData(TankViewer.Member, Followers, true)]
    [InlineData(TankViewer.Member, Selected, false)]
    [InlineData(TankViewer.Member, Private, false)]
    [InlineData(TankViewer.Follower, Followers, true)]
    [InlineData(TankViewer.Selected, Followers, true)]
    [InlineData(TankViewer.Anonymous, Public, false)]
    [InlineData(TankViewer.Anonymous, Followers, false)]
    [InlineData(TankViewer.Owner, Public, false)]
    public void CanFollow_NeedsAnAccountAndAProfileThatTakesFollowers(TankViewer viewer, TankVisibility profile, bool expected) =>
        Assert.Equal(expected, TankVisibilityRules.CanFollow(viewer, profile));

    [Fact]
    public void ViewerOf_ClassifiesTheCallerAgainstTheOwner()
    {
        Assert.Equal(TankViewer.Anonymous, TankVisibilityRules.ViewerOf(null, 5));
        Assert.Equal(TankViewer.Member, TankVisibilityRules.ViewerOf(7, 5));
        Assert.Equal(TankViewer.Owner, TankVisibilityRules.ViewerOf(5, 5));
        Assert.Equal(TankViewer.Follower, TankVisibilityRules.ViewerOf(7, 5, isApprovedFollower: true));
        Assert.Equal(TankViewer.Selected, TankVisibilityRules.ViewerOf(7, 5, isSelectedMember: true));
        Assert.Equal(TankViewer.Selected, TankVisibilityRules.ViewerOf(7, 5, isApprovedFollower: true, isSelectedMember: true));
        Assert.Equal(TankViewer.Owner, TankVisibilityRules.ViewerOf(5, 5, isApprovedFollower: true, isSelectedMember: true));
        Assert.Equal(TankViewer.Anonymous, TankVisibilityRules.ViewerOf(null, 5, isApprovedFollower: true));
    }

    [Fact]
    public void Breadth_OrdersThePublicLevelAboveFollowersAboveSelectedAbovePrivate()
    {
        Assert.True(TankVisibilityRules.Breadth(Public) > TankVisibilityRules.Breadth(Followers));
        Assert.True(TankVisibilityRules.Breadth(Followers) > TankVisibilityRules.Breadth(Selected));
        Assert.True(TankVisibilityRules.Breadth(Selected) > TankVisibilityRules.Breadth(Private));
    }

    [Theory]
    [InlineData(Public, Public, true)]
    [InlineData(Public, Followers, true)]
    [InlineData(Public, Selected, true)]
    [InlineData(Followers, Followers, true)]
    [InlineData(Followers, Selected, true)]
    [InlineData(Followers, Public, false)]
    [InlineData(Selected, Followers, false)]
    [InlineData(Selected, Selected, true)]
    [InlineData(Selected, Private, true)]
    [InlineData(Private, Selected, false)]
    public void IsStreamWithinProfile_UsesTheFullOrderOfTheLevels(TankVisibility profile, TankVisibility stream, bool expected) =>
        Assert.Equal(expected, TankVisibilityRules.IsStreamWithinProfile(profile, stream));

    [Theory]
    [InlineData(TankViewer.Anonymous, Followers, false)]
    [InlineData(TankViewer.Member, Followers, false)]
    [InlineData(TankViewer.Follower, Followers, true)]
    [InlineData(TankViewer.Selected, Followers, true)]
    [InlineData(TankViewer.Owner, Followers, true)]
    [InlineData(TankViewer.Member, Selected, false)]
    [InlineData(TankViewer.Follower, Selected, false)]
    [InlineData(TankViewer.Selected, Selected, true)]
    [InlineData(TankViewer.Owner, Selected, true)]
    [InlineData(TankViewer.Selected, Private, false)]
    [InlineData(TankViewer.Follower, Public, true)]
    public void Allows_ReachesFollowersAndSelectedMembersAtTheirLevels(TankViewer viewer, TankVisibility level, bool expected) =>
        Assert.Equal(expected, TankVisibilityRules.Allows(level, viewer));

    [Theory]
    [InlineData(TankViewer.Follower, Followers, Followers, true)]
    [InlineData(TankViewer.Follower, Followers, Selected, false)]
    [InlineData(TankViewer.Selected, Followers, Selected, true)]
    [InlineData(TankViewer.Selected, Selected, Selected, true)]
    [InlineData(TankViewer.Selected, Selected, Private, false)]
    [InlineData(TankViewer.Member, Public, Followers, false)]
    [InlineData(TankViewer.Follower, Public, Followers, true)]
    public void CanSeeStream_NeedsTheViewerInTheAudienceOfBothLevels(TankViewer viewer, TankVisibility profile, TankVisibility stream, bool expected) =>
        Assert.Equal(expected, TankVisibilityRules.CanSeeStream(viewer, profile, stream));

    [Fact]
    public void ANewTankEntityIsPublicForProfileAndStream()
    {
        var tank = new Tank();
        Assert.Equal(Public, tank.ProfileVisibility);
        Assert.Equal(Public, tank.StreamVisibility);
    }

    [Fact]
    public void ProfileVisibleTo_SelectsPublicTanksAndTheOwnTanksOfTheViewer()
    {
        var tanks = new[]
        {
            new Tank { Id = 1, ProfileId = 10, ProfileVisibility = Public, StreamVisibility = Public },
            new Tank { Id = 2, ProfileId = 10, ProfileVisibility = Private, StreamVisibility = Private },
            new Tank { Id = 3, ProfileId = 11, ProfileVisibility = Private, StreamVisibility = Private },
        }.AsQueryable();

        Assert.Equal([1], tanks.Where(TankVisibilityRules.ProfileVisibleTo(NoFollows, NoSelected, null)).Select(t => t.Id));
        Assert.Equal([1], tanks.Where(TankVisibilityRules.ProfileVisibleTo(NoFollows, NoSelected, 99)).Select(t => t.Id));
        Assert.Equal([1, 2], tanks.Where(TankVisibilityRules.ProfileVisibleTo(NoFollows, NoSelected, 10)).Select(t => t.Id));
        Assert.Equal([1, 3], tanks.Where(TankVisibilityRules.ProfileVisibleTo(NoFollows, NoSelected, 11)).Select(t => t.Id));
    }

    [Fact]
    public void StreamVisibleTo_NeedsAPublicProfileAndAPublicStreamForOthers()
    {
        var tanks = new[]
        {
            new Tank { Id = 1, ProfileId = 10, ProfileVisibility = Public, StreamVisibility = Public },
            new Tank { Id = 2, ProfileId = 10, ProfileVisibility = Public, StreamVisibility = Private },
            new Tank { Id = 3, ProfileId = 10, ProfileVisibility = Private, StreamVisibility = Private },
        }.AsQueryable();

        Assert.Equal([1], tanks.Where(TankVisibilityRules.StreamVisibleTo(NoFollows, NoSelected, null)).Select(t => t.Id));
        Assert.Equal([1], tanks.Where(TankVisibilityRules.StreamVisibleTo(NoFollows, NoSelected, 99)).Select(t => t.Id));
        Assert.Equal([1, 2, 3], tanks.Where(TankVisibilityRules.StreamVisibleTo(NoFollows, NoSelected, 10)).Select(t => t.Id));
    }

    private static readonly IQueryable<Follow> NoFollows = Array.Empty<Follow>().AsQueryable();
    private static readonly IQueryable<TankSelectedMember> NoSelected = Array.Empty<TankSelectedMember>().AsQueryable();

    [Fact]
    public void Predicates_ReachApprovedFollowersAndSelectedMembersAtTheirLevels()
    {
        var tanks = new[]
        {
            new Tank { Id = 1, ProfileId = 10, ProfileVisibility = Followers, StreamVisibility = Followers },
            new Tank { Id = 2, ProfileId = 10, ProfileVisibility = Followers, StreamVisibility = Selected },
            new Tank { Id = 3, ProfileId = 10, ProfileVisibility = Selected, StreamVisibility = Selected },
            new Tank { Id = 4, ProfileId = 10, ProfileVisibility = Public, StreamVisibility = Followers },
        }.AsQueryable();
        var follows = new[]
        {
            new Follow { FollowerProfileId = 20, TargetTankId = 1, Status = FollowStatus.Approved },
            new Follow { FollowerProfileId = 20, TargetTankId = 2, Status = FollowStatus.Approved },
            new Follow { FollowerProfileId = 20, TargetTankId = 3, Status = FollowStatus.Approved },
            new Follow { FollowerProfileId = 20, TargetTankId = 4, Status = FollowStatus.Approved },
            new Follow { FollowerProfileId = 21, TargetTankId = 1, Status = FollowStatus.Pending },
            new Follow { FollowerProfileId = 21, TargetTankId = 4, Status = FollowStatus.Pending },
        }.AsQueryable();
        var selected = new[]
        {
            new TankSelectedMember { TankId = 2, MemberProfileId = 22 },
            new TankSelectedMember { TankId = 3, MemberProfileId = 22 },
            new TankSelectedMember { TankId = 1, MemberProfileId = 22 },
        }.AsQueryable();

        long[] Profile(long? viewer) => [.. tanks.Where(TankVisibilityRules.ProfileVisibleTo(follows, selected, viewer)).Select(t => t.Id)];
        long[] Stream(long? viewer) => [.. tanks.Where(TankVisibilityRules.StreamVisibleTo(follows, selected, viewer)).Select(t => t.Id)];

        Assert.Equal([1, 2, 4], Profile(20));
        Assert.Equal([1, 2, 3, 4], Profile(10));
        Assert.Equal([4], Profile(21));
        Assert.Equal([1, 2, 3, 4], Profile(22));
        Assert.Equal([4], Profile(23));
        Assert.Equal([4], Profile(null));

        Assert.Equal([1, 4], Stream(20));
        Assert.Equal([1, 2, 3], Stream(22));
        Assert.Equal([1, 2, 3, 4], Stream(10));
        Assert.Empty(Stream(23));
        Assert.Empty(Stream(null));
    }
}
