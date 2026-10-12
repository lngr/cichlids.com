// Story: task-3.32
using Cichlids.Etl.Steps;

namespace Cichlids.Etl.Tests;

public sealed class RatingSelectionTests
{
    private const int Picture = 1;
    private const int Tank = 2;

    [Fact]
    public void KeepsTheNewestRatingOfAMemberOnATarget()
    {
        var result = RatingSelection.Select(
        [
            new RatingCandidate(Uid: 1, Rater: 7, TargetType: Picture, TargetId: 100, CreatedAt: 200, IsSelf: false),
            new RatingCandidate(Uid: 2, Rater: 7, TargetType: Picture, TargetId: 100, CreatedAt: 100, IsSelf: false),
            new RatingCandidate(Uid: 3, Rater: 7, TargetType: Picture, TargetId: 100, CreatedAt: 150, IsSelf: false),
        ]);

        Assert.Equal([1], result.Winners);
        Assert.Equal(2, result.Collapsed);
        Assert.Equal(0, result.SelfDropped);
    }

    [Fact]
    public void GivesATieToTheHigherLegacyUid()
    {
        var result = RatingSelection.Select(
        [
            new RatingCandidate(Uid: 5, Rater: 7, TargetType: Picture, TargetId: 100, CreatedAt: 100, IsSelf: false),
            new RatingCandidate(Uid: 9, Rater: 7, TargetType: Picture, TargetId: 100, CreatedAt: 100, IsSelf: false),
            new RatingCandidate(Uid: 3, Rater: 7, TargetType: Picture, TargetId: 100, CreatedAt: 100, IsSelf: false),
        ]);

        Assert.Equal([9], result.Winners);
        Assert.Equal(2, result.Collapsed);
    }

    [Fact]
    public void KeepsOneRatingPerTargetAndPerMember()
    {
        var result = RatingSelection.Select(
        [
            new RatingCandidate(Uid: 1, Rater: 7, TargetType: Picture, TargetId: 100, CreatedAt: 100, IsSelf: false),
            new RatingCandidate(Uid: 2, Rater: 7, TargetType: Tank, TargetId: 100, CreatedAt: 100, IsSelf: false),
            new RatingCandidate(Uid: 3, Rater: 7, TargetType: Picture, TargetId: 101, CreatedAt: 100, IsSelf: false),
            new RatingCandidate(Uid: 4, Rater: 8, TargetType: Picture, TargetId: 100, CreatedAt: 100, IsSelf: false),
        ]);

        Assert.Equal([1, 2, 3, 4], result.Winners.Order());
        Assert.Equal(0, result.Collapsed);
    }

    [Fact]
    public void DropsEverySelfRatingBeforeTheNewestRatingIsChosen()
    {
        var result = RatingSelection.Select(
        [
            new RatingCandidate(Uid: 1, Rater: 7, TargetType: Picture, TargetId: 100, CreatedAt: 100, IsSelf: true),
            new RatingCandidate(Uid: 2, Rater: 7, TargetType: Picture, TargetId: 100, CreatedAt: 200, IsSelf: true),
            new RatingCandidate(Uid: 3, Rater: 8, TargetType: Picture, TargetId: 100, CreatedAt: 100, IsSelf: false),
        ]);

        Assert.Equal([3], result.Winners);
        Assert.Equal(2, result.SelfDropped);
        Assert.Equal(0, result.Collapsed);
    }

    [Fact]
    public void KeepsEveryAnonymousRatingUnchanged()
    {
        var result = RatingSelection.Select(
        [
            new RatingCandidate(Uid: 1, Rater: 0, TargetType: Tank, TargetId: 100, CreatedAt: 100, IsSelf: false),
            new RatingCandidate(Uid: 2, Rater: 0, TargetType: Tank, TargetId: 100, CreatedAt: 200, IsSelf: false),
            new RatingCandidate(Uid: 3, Rater: 0, TargetType: Tank, TargetId: 100, CreatedAt: 300, IsSelf: true),
        ]);

        Assert.Equal([1, 2, 3], result.Winners.Order());
        Assert.Equal(0, result.Collapsed);
        Assert.Equal(0, result.SelfDropped);
    }
}
