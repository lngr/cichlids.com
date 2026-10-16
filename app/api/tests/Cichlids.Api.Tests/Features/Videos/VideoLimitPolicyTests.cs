using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Cichlids.Api.Tests.Features.Videos;

[Collection(ApiCollection.Name)]
public sealed class VideoLimitPolicyTests(ApiFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static VideoLimitPolicy Policy(CichlidsDbContext db, VideoLimitOptions? options = null) =>
        new(db, Options.Create(options ?? new VideoLimitOptions()), new FakeTimeProvider(Now));

    private async Task<long> MemberAsync(DateTimeOffset createdAt, int publicPictures, int drafts = 0, int deleted = 0)
    {
        await using var db = fixture.CreateDbContext();
        var profile = new Profile { Username = $"video-limit-{Guid.NewGuid():N}"[..30], Kind = ProfileKind.Member, CreatedAt = createdAt };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        void Add(int count, PostState state, DateTimeOffset? deletedAt = null)
        {
            for (var i = 0; i < count; i++)
            {
                db.Posts.Add(new Post
                {
                    AuthorProfileId = profile.Id, Kind = PostKind.Single, State = state, DeletedAt = deletedAt,
                    CreatedAt = Now, UpdatedAt = Now, PublishedAt = state == PostState.Published ? Now : null,
                });
            }
        }

        Add(publicPictures, PostState.Published);
        Add(drafts, PostState.Draft);
        Add(deleted, PostState.Published, Now);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    [Fact]
    public async Task GivenAnOldAccountWithMoreThan20PublicPictures_ThenTheLongLimitApplies()
    {
        var id = await MemberAsync(Now.AddYears(-1).AddSeconds(-1), publicPictures: 21);
        await using var db = fixture.CreateDbContext();

        var limits = await Policy(db).ResolveAsync(id, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(3), limits.MaxDuration);
    }

    [Fact]
    public async Task GivenAccountAgeExactlyOneYear_ThenTheShortLimitApplies()
    {
        var id = await MemberAsync(Now.AddYears(-1), publicPictures: 21);
        await using var db = fixture.CreateDbContext();

        Assert.Equal(TimeSpan.FromSeconds(60), (await Policy(db).ResolveAsync(id, CancellationToken.None)).MaxDuration);
    }

    [Fact]
    public async Task GivenExactly20PublicPictures_ThenTheShortLimitApplies()
    {
        var id = await MemberAsync(Now.AddYears(-3), publicPictures: 20);
        await using var db = fixture.CreateDbContext();

        Assert.Equal(TimeSpan.FromSeconds(60), (await Policy(db).ResolveAsync(id, CancellationToken.None)).MaxDuration);
    }

    [Fact]
    public async Task GivenAYoungAccountWithManyPictures_ThenTheShortLimitApplies()
    {
        var id = await MemberAsync(Now.AddMonths(-2), publicPictures: 100);
        await using var db = fixture.CreateDbContext();

        Assert.Equal(TimeSpan.FromSeconds(60), (await Policy(db).ResolveAsync(id, CancellationToken.None)).MaxDuration);
    }

    [Fact]
    public async Task GivenAnOldAccountWithFewPictures_ThenTheShortLimitApplies()
    {
        var id = await MemberAsync(Now.AddYears(-3), publicPictures: 3);
        await using var db = fixture.CreateDbContext();

        Assert.Equal(TimeSpan.FromSeconds(60), (await Policy(db).ResolveAsync(id, CancellationToken.None)).MaxDuration);
    }

    [Fact]
    public async Task OnlyPublishedAndNotDeletedPicturesCount()
    {
        var id = await MemberAsync(Now.AddYears(-3), publicPictures: 15, drafts: 10, deleted: 10);
        await using var db = fixture.CreateDbContext();

        Assert.Equal(TimeSpan.FromSeconds(60), (await Policy(db).ResolveAsync(id, CancellationToken.None)).MaxDuration);
    }

    [Fact]
    public async Task TheLimitsAreConfigurationValues()
    {
        var id = await MemberAsync(Now.AddYears(-3), publicPictures: 3);
        await using var db = fixture.CreateDbContext();
        var options = new VideoLimitOptions { ShortMaxDuration = TimeSpan.FromSeconds(30), MaxDuration = TimeSpan.FromMinutes(2), MinPublicPictures = 2 };

        var limits = await Policy(db, options).ResolveAsync(id, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(2), limits.MaxDuration);
    }

    [Theory]
    [InlineData(180, 25_000_000, 562_500_000)]
    [InlineData(60, 25_000_000, 187_500_000)]
    [InlineData(1, 8, 1)]
    public void TheSizeLimitIsDerivedFromTheLengthAndTheMaximumBitrate(int seconds, long bitsPerSecond, long expected)
    {
        var limits = VideoLimits.For(TimeSpan.FromSeconds(seconds), bitsPerSecond);

        Assert.Equal(expected, limits.MaxBytes);
    }

    [Fact]
    public void TheDefaultsGiveEveryMemberTheShortLimitUntilTheRuleIsMet()
    {
        var options = new VideoLimitOptions();

        Assert.Equal(TimeSpan.FromMinutes(3), options.MaxDuration);
        Assert.Equal(TimeSpan.FromSeconds(60), options.ShortMaxDuration);
        Assert.Equal(TimeSpan.FromDays(365), options.MinAccountAge);
        Assert.Equal(20, options.MinPublicPictures);
    }
}
