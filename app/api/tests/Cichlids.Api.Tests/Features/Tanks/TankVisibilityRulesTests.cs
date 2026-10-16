using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Tanks;

public sealed class TankVisibilityRulesTests
{
    private const TankVisibility Public = TankVisibility.Public;
    private const TankVisibility Private = TankVisibility.Private;

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
    [InlineData(TankViewer.Member, Private, false)]
    [InlineData(TankViewer.Anonymous, Public, false)]
    [InlineData(TankViewer.Owner, Public, false)]
    public void CanFollow_NeedsAnAccountAndAPublicProfile(TankViewer viewer, TankVisibility profile, bool expected) =>
        Assert.Equal(expected, TankVisibilityRules.CanFollow(viewer, profile));

    [Fact]
    public void ViewerOf_ClassifiesTheCallerAgainstTheOwner()
    {
        Assert.Equal(TankViewer.Anonymous, TankVisibilityRules.ViewerOf(null, 5));
        Assert.Equal(TankViewer.Member, TankVisibilityRules.ViewerOf(7, 5));
        Assert.Equal(TankViewer.Owner, TankVisibilityRules.ViewerOf(5, 5));
    }

    [Fact]
    public void ANewTankEntityIsPublicForProfileAndStream()
    {
        var tank = new Tank();
        Assert.Equal(Public, tank.ProfileVisibility);
        Assert.Equal(Public, tank.StreamVisibility);
        Assert.True(tank.MediaPlacedPublic);
    }

    [Theory]
    [InlineData(Public, true)]
    [InlineData(Private, false)]
    public void MediaIsPublicOnlyForAPublicProfile(TankVisibility profile, bool expected) =>
        Assert.Equal(expected, TankVisibilityRules.MediaBelongsInPublicStorage(profile));

    [Fact]
    public void ProfileVisibleTo_SelectsPublicTanksAndTheOwnTanksOfTheViewer()
    {
        var tanks = new[]
        {
            new Tank { Id = 1, ProfileId = 10, ProfileVisibility = Public, StreamVisibility = Public },
            new Tank { Id = 2, ProfileId = 10, ProfileVisibility = Private, StreamVisibility = Private },
            new Tank { Id = 3, ProfileId = 11, ProfileVisibility = Private, StreamVisibility = Private },
        }.AsQueryable();

        Assert.Equal([1], tanks.Where(TankVisibilityRules.ProfileVisibleTo(null)).Select(t => t.Id));
        Assert.Equal([1], tanks.Where(TankVisibilityRules.ProfileVisibleTo(99)).Select(t => t.Id));
        Assert.Equal([1, 2], tanks.Where(TankVisibilityRules.ProfileVisibleTo(10)).Select(t => t.Id));
        Assert.Equal([1, 3], tanks.Where(TankVisibilityRules.ProfileVisibleTo(11)).Select(t => t.Id));
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

        Assert.Equal([1], tanks.Where(TankVisibilityRules.StreamVisibleTo(null)).Select(t => t.Id));
        Assert.Equal([1], tanks.Where(TankVisibilityRules.StreamVisibleTo(99)).Select(t => t.Id));
        Assert.Equal([1, 2, 3], tanks.Where(TankVisibilityRules.StreamVisibleTo(10)).Select(t => t.Id));
    }
}
