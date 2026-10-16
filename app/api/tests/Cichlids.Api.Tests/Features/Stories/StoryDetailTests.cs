using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Stories;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Stories;

[Collection(ApiCollection.Name)]
public class StoryDetailTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<long> _storyIds = [];
    private readonly List<long> _tankIds = [];
    private readonly List<long> _speciesIds = [];
    private readonly List<long> _mediaItemIds = [];
    private readonly List<long> _profileIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Removes the rows of the test so the shared database keeps the seeded content other tests count.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Follows.Where(f => f.TargetTankId != null && _tankIds.Contains(f.TargetTankId.Value)).ExecuteDeleteAsync();
        await db.TankSelectedMembers.Where(s => _tankIds.Contains(s.TankId)).ExecuteDeleteAsync();
        await db.SlugAliases.Where(s => _storyIds.Contains(s.StoryId)).ExecuteDeleteAsync();
        await db.Stories.Where(s => _storyIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Tanks.Where(t => _tankIds.Contains(t.Id)).ExecuteDeleteAsync();
        await db.Species.Where(s => _speciesIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.MediaVariants.Where(v => _mediaItemIds.Contains(v.MediaItemId)).ExecuteDeleteAsync();
        await db.MediaItems.Where(m => _mediaItemIds.Contains(m.Id)).ExecuteDeleteAsync();
        await db.Profiles.Where(p => _profileIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private sealed record Seeded(
        Story Story, string Slug, Tank Tank, Inhabitant Inhabitant, Cichlids.Domain.Entities.Species Species, MediaLink[] Items, long OwnerProfileId, string OwnerToken);

    private async Task<(Profile Profile, string Token)> MemberAsync()
    {
        await using var db = fixture.CreateDbContext();
        var subject = Guid.NewGuid().ToString();
        var profile = new Profile
        {
            Username = $"u{subject[..12]}", DisplayName = "Story Member", CreatedAt = DateTimeOffset.UtcNow,
            Identities = [new ProfileIdentity { Provider = "oidc", Subject = subject, CreatedAt = DateTimeOffset.UtcNow }],
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        _profileIds.Add(profile.Id);
        return (profile, TestTokens.Create(subject, subject));
    }

    private async Task<MediaItem> PhotoAsync(Cichlids.Infrastructure.Persistence.CichlidsDbContext db, long owner, string name)
    {
        var item = new MediaItem { OwnerProfileId = owner, Kind = MediaKind.Photo, StorageKey = $"originals/story/{name}.jpg", CreatedAt = DateTimeOffset.UtcNow };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        db.MediaVariants.AddRange(
            new MediaVariant { MediaItemId = item.Id, Label = "thumb", Width = 200, StorageKey = $"variants/thumb/story/{name}.jpg" },
            new MediaVariant { MediaItemId = item.Id, Label = "small", Width = 400, StorageKey = $"variants/small/story/{name}.jpg" });
        await db.SaveChangesAsync();
        _mediaItemIds.Add(item.Id);
        return item;
    }

    /// <summary>
    /// A published story of three photos with captions, a tank card at position 1 (between the first and
    /// the second photo), a species reference and the inhabitant of the tank as a card at position 4, and
    /// the third photo chosen as the cover.
    /// </summary>
    private async Task<Seeded> SeedAsync(TankVisibility tankVisibility, bool chooseCover = true)
    {
        var (owner, token) = await MemberAsync();
        await using var raw = fixture.CreateDbContext();
        var tank = new Tank
        {
            ProfileId = owner.Id, Title = $"Story tank {Guid.NewGuid():N}", State = TankState.Published, CreatedAt = DateTimeOffset.UtcNow,
            PublishedAt = DateTimeOffset.UtcNow, ProfileVisibility = tankVisibility,
            StreamVisibility = TankVisibility.Private,
        };
        var species = new Cichlids.Domain.Entities.Species
        {
            Genus = $"Story{Guid.NewGuid():N}", Name = "test", DisplayName = "Story test",
        };
        raw.AddRange(tank, species);
        await raw.SaveChangesAsync();
        _tankIds.Add(tank.Id);
        _speciesIds.Add(species.Id);
        var inhabitant = new Inhabitant { TankId = tank.Id, SpeciesId = species.Id, Count = 4, Sort = 0 };
        raw.Inhabitants.Add(inhabitant);
        var story = new Story
        {
            AuthorProfileId = owner.Id, Topic = StoryTopic.Cichlids, Title = "Three photos", Description = "A story",
            State = StoryState.Published, CreatedAt = DateTimeOffset.UtcNow, PublishedAt = DateTimeOffset.UtcNow,
        };
        raw.Stories.Add(story);
        await raw.SaveChangesAsync();
        _storyIds.Add(story.Id);
        var slug = $"s{Guid.NewGuid():N}"[..9];
        raw.SlugAliases.Add(new SlugAlias { StoryId = story.Id, Value = slug, IsCanonical = true, CreatedAt = DateTimeOffset.UtcNow });

        var items = new MediaLink[3];
        var positions = new[] { 0, 2, 3 };
        for (var i = 0; i < 3; i++)
        {
            var photo = await PhotoAsync(raw, owner.Id, $"{Guid.NewGuid():N}");
            items[i] = MediaLink.ForStory(story.Id, photo.Id, positions[i]);
            items[i].Caption = $"Caption {i + 1}";
            raw.MediaLinks.Add(items[i]);
        }

        await raw.SaveChangesAsync();
        if (chooseCover)
        {
            story.CoverMediaLinkId = items[2].Id;
        }

        raw.StorySubjects.AddRange(
            new StorySubject { StoryId = story.Id, SubjectType = StorySubjectType.Tank, TankId = tank.Id, Position = 1 },
            new StorySubject { StoryId = story.Id, SubjectType = StorySubjectType.Inhabitant, InhabitantId = inhabitant.Id, Position = 4 },
            new StorySubject { StoryId = story.Id, SubjectType = StorySubjectType.Species, SpeciesId = species.Id });
        await raw.SaveChangesAsync();
        return new Seeded(story, slug, tank, inhabitant, species, items, owner.Id, token);
    }

    private async Task<StoryDetailDto> DetailAsync(Seeded seeded, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/stories/{seeded.Slug}");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<StoryDetailDto>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;
    }

    [Fact]
    public async Task GivenAnArchivedPicture_WhenAVisitorOpensIt_ThenItIsAStoryWithOneItemAtTheSameAddress()
    {
        var response = await fixture.Client.GetAsync($"/api/stories/{fixture.Seed.PublishedPictureACanonicalSlug}");
        response.EnsureSuccessStatusCode();
        var detail = JsonSerializer.Deserialize<StoryDetailDto>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal("Reef Tank Beauty", detail.Title);
        Assert.Equal("A colorful cichlid", detail.Description);
        var item = Assert.Single(detail.Items);
        Assert.Null(item.Caption);
        Assert.Equal(MediaKind.Photo, item.Kind);
        Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, item.Image!.Thumb);
        Assert.Equal(item.Id, detail.CoverItemId);
        Assert.Equal(item.Image.Thumb, detail.Cover!.Thumb);
        Assert.All(detail.Subjects, s => Assert.Equal(StorySubjectType.Species, s.Type));
    }

    [Fact]
    public async Task GivenAStoryWithThreePhotosATankCardAndAChosenCover_WhenAVisitorOpensTheGallery_ThenTheEntryShowsTheCover()
    {
        var seeded = await SeedAsync(TankVisibility.Public);

        var response = await fixture.Client.GetAsync($"/api/stories?user={seeded.OwnerProfileId}");
        response.EnsureSuccessStatusCode();
        var list = JsonSerializer.Deserialize<PagedResponse<StoryListItemDto>>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        var entry = Assert.Single(list.Items);
        Assert.Equal(3, entry.ItemCount);
        await using var db = fixture.CreateDbContext();
        var coverMedia = await db.MediaLinks.Where(l => l.Id == seeded.Items[2].Id).Select(l => l.MediaItemId).SingleAsync();
        var coverThumb = await db.MediaVariants.Where(v => v.MediaItemId == coverMedia && v.Label == "thumb").Select(v => v.StorageKey).SingleAsync();
        Assert.EndsWith(coverThumb, entry.Cover!.Thumb);
    }

    [Fact]
    public async Task GivenAStoryWithoutAChosenCover_WhenAVisitorOpensTheGallery_ThenTheEntryShowsTheFirstItem()
    {
        var seeded = await SeedAsync(TankVisibility.Public, chooseCover: false);

        var detail = await DetailAsync(seeded);

        Assert.Equal(seeded.Items[0].Id, detail.CoverItemId);
        Assert.Equal(detail.Items[0].Image!.Thumb, detail.Cover!.Thumb);
    }

    [Fact]
    public async Task GivenAStoryWithThreePhotosAndATankCard_WhenAVisitorOpensTheStory_ThenItemsAndCardsComeWithTheirCaptionsAndPositions()
    {
        var seeded = await SeedAsync(TankVisibility.Public);

        var detail = await DetailAsync(seeded);

        Assert.Equal(["Caption 1", "Caption 2", "Caption 3"], detail.Items.Select(i => i.Caption));
        Assert.Equal([0, 2, 3], detail.Items.Select(i => i.Position));
        Assert.Equal(seeded.Items[2].Id, detail.CoverItemId);
        Assert.All(detail.Items, i => Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, i.Image!.Thumb));

        Assert.Equal([StorySubjectType.Tank, StorySubjectType.Inhabitant, StorySubjectType.Species], detail.Subjects.Select(s => s.Type));
        Assert.Equal([1, 4, null], detail.Subjects.Select(s => s.Position));
        Assert.Equal(seeded.Tank.Id, detail.Subjects[0].Tank!.Id);
        Assert.Equal(seeded.Tank.Title, detail.Subjects[0].Tank!.Title);
        Assert.Equal(4, detail.Subjects[1].Inhabitant!.Count);
        Assert.Equal(seeded.Species.Id, detail.Subjects[1].Inhabitant!.Species!.Id);
        Assert.Equal(seeded.Species.Id, detail.Subjects[2].Species!.Id);
    }

    [Fact]
    public async Task GivenAStoryWhoseTankSubjectIsPrivate_WhenAVisitorWithoutAccessOpensIt_ThenTheStoryShowsWithoutThatCard()
    {
        var seeded = await SeedAsync(TankVisibility.Private);
        var (_, stranger) = await MemberAsync();

        var anonymous = await DetailAsync(seeded);
        var member = await DetailAsync(seeded, stranger);

        foreach (var detail in new[] { anonymous, member })
        {
            Assert.Equal(3, detail.Items.Count);
            var subject = Assert.Single(detail.Subjects);
            Assert.Equal(StorySubjectType.Species, subject.Type);
        }
    }

    [Fact]
    public async Task GivenAStoryWhoseTankSubjectIsPrivate_WhenTheOwnerOpensIt_ThenTheCardsShow()
    {
        var seeded = await SeedAsync(TankVisibility.Private);

        var detail = await DetailAsync(seeded, seeded.OwnerToken);

        Assert.Equal([StorySubjectType.Tank, StorySubjectType.Inhabitant, StorySubjectType.Species], detail.Subjects.Select(s => s.Type));
    }

    [Fact]
    public async Task GivenAStoryWhoseTankSubjectIsPrivate_WhenTheTankIsMadePublic_ThenTheCardAppearsForAVisitor()
    {
        var seeded = await SeedAsync(TankVisibility.Private);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == seeded.Tank.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ProfileVisibility, TankVisibility.Public));
        }

        var detail = await DetailAsync(seeded);

        Assert.Equal(3, detail.Subjects.Count);
    }

    [Fact]
    public async Task GivenStoriesOfTwoSpecies_WhenFilteringTheGalleryBySpecies_ThenOnlyStoriesWithThatSpeciesSubjectShow()
    {
        var seeded = await SeedAsync(TankVisibility.Public);
        var other = await SeedAsync(TankVisibility.Public);

        var response = await fixture.Client.GetAsync($"/api/stories?species={seeded.Species.Id}");
        response.EnsureSuccessStatusCode();
        var list = JsonSerializer.Deserialize<PagedResponse<StoryListItemDto>>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Contains(list.Items, i => i.Id == seeded.Story.Id);
        Assert.DoesNotContain(list.Items, i => i.Id == other.Story.Id);
    }

    [Theory]
    [InlineData(TankVisibility.Public, "anonymous", true)]
    [InlineData(TankVisibility.Public, "stranger", true)]
    [InlineData(TankVisibility.Followers, "anonymous", false)]
    [InlineData(TankVisibility.Followers, "stranger", false)]
    [InlineData(TankVisibility.Followers, "pending", false)]
    [InlineData(TankVisibility.Followers, "follower", true)]
    [InlineData(TankVisibility.Followers, "selected", true)]
    [InlineData(TankVisibility.Followers, "owner", true)]
    [InlineData(TankVisibility.Selected, "anonymous", false)]
    [InlineData(TankVisibility.Selected, "stranger", false)]
    [InlineData(TankVisibility.Selected, "follower", false)]
    [InlineData(TankVisibility.Selected, "selected", true)]
    [InlineData(TankVisibility.Selected, "owner", true)]
    [InlineData(TankVisibility.Private, "anonymous", false)]
    [InlineData(TankVisibility.Private, "selected", false)]
    [InlineData(TankVisibility.Private, "owner", true)]
    public async Task GivenATankLevel_WhenAViewerOpensTheStory_ThenTheTankAndInhabitantCardsShowOnlyToTheAudienceOfTheLevel(
        TankVisibility level, string viewer, bool sees)
    {
        var seeded = await SeedAsync(level);
        Profile? profile = null;
        string? token = viewer == "owner" ? seeded.OwnerToken : null;
        if (viewer is not ("anonymous" or "owner"))
        {
            (profile, token) = await MemberAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            if (viewer == "follower" || viewer == "pending")
            {
                db.Follows.Add(new Follow
                {
                    FollowerProfileId = profile!.Id, TargetTankId = seeded.Tank.Id, CreatedAt = DateTimeOffset.UtcNow,
                    Status = viewer == "pending" ? FollowStatus.Pending : FollowStatus.Approved,
                });
            }
            else if (viewer == "selected")
            {
                db.TankSelectedMembers.Add(new TankSelectedMember { TankId = seeded.Tank.Id, MemberProfileId = profile!.Id, AddedAt = DateTimeOffset.UtcNow });
            }

            await db.SaveChangesAsync();
        }

        var detail = await DetailAsync(seeded, token);

        Assert.Equal(3, detail.Items.Count);
        var types = detail.Subjects.Select(s => s.Type).ToList();
        Assert.Contains(StorySubjectType.Species, types);
        Assert.Equal(sees, types.Contains(StorySubjectType.Tank));
        Assert.Equal(sees, types.Contains(StorySubjectType.Inhabitant));
    }
}
