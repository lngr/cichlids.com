using System.Data.Common;
using Cichlids.Api.Features.Me;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Streaks.StreakTestSupport;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Me;

/// <summary>
/// The queries of the followed tanks: their number does not grow with the page, and an update that
/// disappears while the page is read leaves the tank without an update.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-5.24")]
public sealed class TodayFollowedQueryTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<long> _stories = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Stories.Where(s => _stories.Contains(s.Id)).ExecuteDeleteAsync();
        var tanks = db.Tanks.Where(t => db.Profiles.Any(p => p.Id == t.ProfileId && p.Username.StartsWith("vis-")));
        await db.Follows.Where(f => tanks.Any(t => t.Id == f.TargetTankId)).ExecuteDeleteAsync();
        await db.CareEntries.Where(e => tanks.Any(t => t.Id == e.TankId)).ExecuteDeleteAsync();
        await RemoveEventsAsync(fixture, "streak_freeze");
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];
        public Func<string, int, Task>? Before { get; set; }
        public int Count => _commands.Count;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _commands.Add(command.CommandText);
            if (Before is not null) await Before(command.CommandText, _commands.Count(c => c.Contains("story_subject")));
            return result;
        }
    }

    private FollowedTanksService ServiceFor(CommandCounter counter, out CichlidsDbContextHolder holder)
    {
        holder = new CichlidsDbContextHolder(fixture.CreateDbContext(counter));
        return new FollowedTanksService(holder.Db, fixture.Services.GetRequiredService<IObjectStore>(), TimeProvider.System);
    }

    private sealed class CichlidsDbContextHolder(Cichlids.Infrastructure.Persistence.CichlidsDbContext db) : IAsyncDisposable
    {
        public Cichlids.Infrastructure.Persistence.CichlidsDbContext Db { get; } = db;
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private async Task<long> TankWithUpdateAsync(Member viewer, int kind, int hoursAgo)
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var at = DateTimeOffset.UtcNow.AddHours(-hoursAgo);
        await using var db = fixture.CreateDbContext();
        db.Follows.Add(new Follow { FollowerProfileId = viewer.ProfileId, TargetTankId = tank, Status = FollowStatus.Approved, CreatedAt = at, DecidedAt = at });
        if (kind == 0)
        {
            var story = new Story
            {
                AuthorProfileId = owner.ProfileId, Title = "Story", State = StoryState.Published, CreatedAt = at, UpdatedAt = at, PublishedAt = at,
                Subjects = [StorySubject.ForTank(tank)],
            };
            db.Stories.Add(story);
            await db.SaveChangesAsync();
            _stories.Add(story.Id);
        }
        else if (kind == 1)
        {
            var item = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, OwnerProfileId = owner.ProfileId, CreatedAt = at, ReceivedAt = at };
            db.MediaItems.Add(item);
            await db.SaveChangesAsync();
            var link = MediaLink.ForTank(tank, item.Id, MediaLinkRole.TimeLapse);
            link.CreatedAt = at;
            db.MediaLinks.Add(link);
            db.MediaVariants.Add(new MediaVariant { MediaItemId = item.Id, Label = "thumb", StorageKey = $"variants/thumb/{item.StorageKey}.jpg", Width = 200 });
        }
        else
        {
            db.CareEntries.Add(new CareEntry { TankId = tank, AuthorProfileId = owner.ProfileId, Kind = CareKind.WaterChange, OccurredAt = at, RecordedAt = at });
        }

        await db.SaveChangesAsync();
        return tank;
    }

    private async Task<int> QueriesForPageOfAsync(int tanks)
    {
        var viewer = await NewMemberAsync(fixture);
        for (var i = 0; i < tanks; i++) await TankWithUpdateAsync(viewer, i % 3, i + 1);
        var counter = new CommandCounter();
        var service = ServiceFor(counter, out var holder);
        await using (holder)
        {
            var page = await service.ListAsync(viewer.ProfileId, 0, tanks, CancellationToken.None);
            Assert.Equal(tanks, page.Items.Count);
            Assert.All(page.Items, item => Assert.NotNull(item.Update));
        }

        return counter.Count;
    }

    [Fact]
    public async Task GivenPagesOfDifferentSize_WhenListingFollowedTanks_ThenTheNumberOfQueriesStaysTheSame()
    {
        var small = await QueriesForPageOfAsync(3);
        var large = await QueriesForPageOfAsync(15);

        Assert.Equal(small, large);
        Assert.InRange(large, 1, 20);
    }

    [Fact]
    public async Task GivenAStoryThatVanishesWhileTheDetailsAreRead_WhenLoadingToday_ThenTheTankHasNoUpdateAndNothingFails()
    {
        var viewer = await NewMemberAsync(fixture);
        var tank = await TankWithUpdateAsync(viewer, 0, 2);
        var counter = new CommandCounter
        {
            Before = async (_, storyQueries) =>
            {
                if (storyQueries != 2) return;
                await using var db = fixture.CreateDbContext();
                await db.Stories.Where(s => _stories.Contains(s.Id)).ExecuteDeleteAsync();
            },
        };
        var service = ServiceFor(counter, out var holder);
        await using (holder)
        {
            var today = await service.GetTodayAsync(viewer.ProfileId, CancellationToken.None);
            Assert.Empty(today.Items);
            Assert.False(today.HasMore);
        }

        _ = tank;
    }

    [Fact]
    public async Task GivenAStoryThatVanishesWhileTheDetailsAreRead_WhenListingFollowedTanks_ThenTheTankIsListedWithoutAnUpdate()
    {
        var viewer = await NewMemberAsync(fixture);
        var tank = await TankWithUpdateAsync(viewer, 0, 2);
        var counter = new CommandCounter
        {
            Before = async (_, storyQueries) =>
            {
                if (storyQueries != 2) return;
                await using var db = fixture.CreateDbContext();
                await db.Stories.Where(s => _stories.Contains(s.Id)).ExecuteDeleteAsync();
            },
        };
        var service = ServiceFor(counter, out var holder);
        await using (holder)
        {
            var page = await service.ListAsync(viewer.ProfileId, 0, 10, CancellationToken.None);
            var item = Assert.Single(page.Items);
            Assert.Equal(tank, item.TankId);
            Assert.Null(item.Update);
        }
    }
}
