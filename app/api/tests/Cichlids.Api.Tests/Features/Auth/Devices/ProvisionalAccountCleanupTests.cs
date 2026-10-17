// Story: TASK-7.4
using System.Security.Cryptography;
using Cichlids.Api.Features.Auth.Devices;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Features.Videos;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Cichlids.Api.Tests.Features.Auth.Devices;

/// <summary>
/// The removal of provisional accounts without a signed request for twelve calendar months: the profile
/// disappears with its tanks, care data, streak data, vacations, media rows, device credentials and stored
/// objects; accounts in use and registered members stay; an installation that signs in during the pass
/// keeps its profile; objects are deleted after the commit.
/// </summary>
[Collection(UploadsCollection.Name)]
[Trait("Story", "task-7.4")]
public sealed class ProvisionalAccountCleanupTests(UploadsFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private WebApplicationFactory<Program> _host = null!;

    public Task InitializeAsync()
    {
        _host = fixture.CreateHost(null, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_clock);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<int> RunAsync()
    {
        using var scope = _host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProvisionalAccountCleanupService>().RunOnceAsync(CancellationToken.None);
    }

    private static async Task<Profile> AddProfileAsync(CichlidsDbContext db, ProfileKind kind, DateTimeOffset createdAt)
    {
        var profile = new Profile
        {
            Username = $"{(kind == ProfileKind.Provisional ? "provisional" : "member")}-{Guid.NewGuid():N}",
            DisplayName = "user",
            Kind = kind,
            CreatedAt = createdAt,
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    private static async Task<DeviceCredential> AddCredentialAsync(CichlidsDbContext db, long profileId, DateTimeOffset lastSignedRequestAt,
        DeviceCredentialState state = DeviceCredentialState.Active)
    {
        var credential = new DeviceCredential
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            PublicKey = RandomNumberGenerator.GetBytes(64),
            Algorithm = "ES256",
            State = state,
            CreatedAt = lastSignedRequestAt,
            LastSignedRequestAt = lastSignedRequestAt,
        };
        db.DeviceCredentials.Add(credential);
        await db.SaveChangesAsync();
        return credential;
    }

    private async Task<Profile> AddProvisionalAsync(DateTimeOffset lastSignedRequestAt)
    {
        await using var db = fixture.CreateDbContext();
        var profile = await AddProfileAsync(db, ProfileKind.Provisional, lastSignedRequestAt);
        await AddCredentialAsync(db, profile.Id, lastSignedRequestAt);
        return profile;
    }

    private static Task<int> SetKindAsync(CichlidsDbContext db, ProfileKind kind, params long[] profileIds) =>
        db.Profiles.Where(p => profileIds.Contains(p.Id)).ExecuteUpdateAsync(s => s.SetProperty(p => p.Kind, kind));

    private async Task<bool> ExistsAsync(long profileId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Profiles.AnyAsync(p => p.Id == profileId);
    }

    private async Task PutAsync(string key) =>
        await fixture.ObjectStore.PutAsync(key, new MemoryStream([1, 2, 3]), "application/octet-stream");

    // A photo with one variant, stored in the object store and in the database.
    private async Task<(MediaItem Item, List<string> Keys)> AddPhotoAsync(CichlidsDbContext db, long ownerId)
    {
        var key = MediaStorageKeys.BuildPrivateOriginalKey("library", ownerId, $"{Guid.NewGuid():N}.jpg");
        var variantKey = MediaStorageKeys.BuildPrivateVariantKey(key, "large");
        await PutAsync(key);
        await PutAsync(variantKey);
        var item = new MediaItem
        {
            OwnerProfileId = ownerId, Kind = MediaKind.Photo, StorageKey = key, CreatedAt = Now, ReceivedAt = Now,
            Variants = [new MediaVariant { Label = "large", StorageKey = variantKey }],
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        return (item, [key, variantKey]);
    }

    // A video with an HLS file under the private variant directory.
    private async Task<(MediaItem Item, List<string> Keys)> AddVideoAsync(CichlidsDbContext db, long ownerId)
    {
        var key = MediaStorageKeys.BuildPrivateVideoKey(ownerId, $"{Guid.NewGuid():N}.mp4");
        var playlist = MediaStorageKeys.BuildVideoDirectory(key, isPrivate: true) + "index.m3u8";
        await PutAsync(key);
        await PutAsync(playlist);
        var item = new MediaItem { OwnerProfileId = ownerId, Kind = MediaKind.Video, StorageKey = key, CreatedAt = Now, ReceivedAt = Now };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        return (item, [key, playlist]);
    }

    // Tank with care data, a water change session, streak data, a vacation, a decommissioning and linked media.
    private async Task<List<string>> AddContentAsync(long profileId)
    {
        await using var db = fixture.CreateDbContext();
        var keys = new List<string>();
        var tank = new Tank
        {
            ProfileId = profileId, Title = "Tank", CreatedAt = Now,
            ProfileVisibility = TankVisibility.Private, StreamVisibility = TankVisibility.Private,
        };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();

        var session = new WaterChangeSession { TankId = tank.Id, ProfileId = profileId, StartedAt = Now };
        db.WaterChangeSessions.Add(session);
        var entry = new CareEntry { TankId = tank.Id, AuthorProfileId = profileId, Kind = CareKind.WaterChange, OccurredAt = Now, RecordedAt = Now };
        db.CareEntries.Add(entry);
        db.CarePlanRules.Add(new CarePlanRule { TankId = tank.Id, Kind = CareKind.WaterChange, RhythmDays = 7, Enabled = true, ReasonCode = "default", UpdatedAt = Now });
        db.Vacations.Add(new Vacation { ProfileId = profileId, StartDate = new DateOnly(2026, 1, 1), CreatedAt = Now });
        db.Inhabitants.Add(new Inhabitant { TankId = tank.Id, OwnerProfileId = profileId, Name = "Bruno", Count = 1, MovedInOn = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync();
        db.StreakFreezes.Add(new StreakFreeze
        {
            TankId = tank.Id, CareEntryId = entry.Id, TankYear = 1, PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 1, 7), CreatedAt = Now,
        });

        var careProof = await AddPhotoAsync(db, profileId);
        var startProof = await AddPhotoAsync(db, profileId);
        var endProof = await AddPhotoAsync(db, profileId);
        var video = await AddVideoAsync(db, profileId);
        var unlinked = await AddPhotoAsync(db, profileId);
        foreach (var added in new[] { careProof, startProof, endProof, video, unlinked })
        {
            keys.AddRange(added.Keys);
        }

        db.MediaLinks.Add(MediaLink.ForCareEntry(entry.Id, careProof.Item.Id, MediaLinkRole.Photo));
        db.MediaLinks.Add(MediaLink.ForTank(tank.Id, video.Item.Id, MediaLinkRole.Video));
        db.MediaLinks.Add(MediaLink.ForWaterChangeSession(session.Id, endProof.Item.Id));
        db.TankDecommissionings.Add(new TankDecommissioning
        {
            TankId = tank.Id, Scope = DecommissionScope.AllCare, StartedAt = Now, StartDate = new DateOnly(2026, 2, 1),
            StartMediaItemId = startProof.Item.Id, EndedAt = Now, EndDate = new DateOnly(2026, 3, 1), EndMediaItemId = endProof.Item.Id,
        });
        await db.SaveChangesAsync();
        return keys;
    }

    private async Task<bool> AnyRowOfAsync(long profileId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Tanks.AnyAsync(t => t.ProfileId == profileId) || await db.CareEntries.AnyAsync(e => e.AuthorProfileId == profileId)
            || await db.Inhabitants.AnyAsync(i => i.OwnerProfileId == profileId) || await db.WaterChangeSessions.AnyAsync(s => s.ProfileId == profileId) || await db.Vacations.AnyAsync(v => v.ProfileId == profileId)
            || await db.MediaItems.AnyAsync(m => m.OwnerProfileId == profileId) || await db.DeviceCredentials.AnyAsync(c => c.ProfileId == profileId)
            || await db.StreakFreezes.AnyAsync(f => db.Tanks.Any(t => t.Id == f.TankId && t.ProfileId == profileId));
    }

    [Fact]
    public async Task A_profile_without_a_signed_request_for_twelve_months_and_a_day_is_removed_with_all_its_content_and_objects()
    {
        var profile = await AddProvisionalAsync(Now.AddMonths(-12).AddDays(-1));
        var keys = await AddContentAsync(profile.Id);
        Assert.True(await AnyRowOfAsync(profile.Id));

        var removed = await RunAsync();

        Assert.True(removed >= 1);
        Assert.False(await ExistsAsync(profile.Id));
        Assert.False(await AnyRowOfAsync(profile.Id));
        foreach (var key in keys)
        {
            Assert.False(await fixture.ObjectStore.ExistsAsync(key), key);
        }
    }

    [Fact]
    public async Task A_profile_used_yesterday_stays_with_its_content_and_objects()
    {
        var profile = await AddProvisionalAsync(Now.AddDays(-1));
        var keys = await AddContentAsync(profile.Id);

        await RunAsync();

        Assert.True(await ExistsAsync(profile.Id));
        Assert.True(await AnyRowOfAsync(profile.Id));
        foreach (var key in keys)
        {
            Assert.True(await fixture.ObjectStore.ExistsAsync(key), key);
        }
    }

    [Fact]
    public async Task Exactly_twelve_months_stay_and_a_second_more_is_removed()
    {
        var exact = await AddProvisionalAsync(Now.AddMonths(-12));
        var older = await AddProvisionalAsync(Now.AddMonths(-12).AddSeconds(-1));

        await RunAsync();

        Assert.True(await ExistsAsync(exact.Id));
        Assert.False(await ExistsAsync(older.Id));
    }

    [Fact]
    public async Task The_most_recent_signed_request_of_any_credential_counts()
    {
        var profile = await AddProvisionalAsync(Now.AddMonths(-20));
        await using (var db = fixture.CreateDbContext())
        {
            await AddCredentialAsync(db, profile.Id, Now.AddMonths(-2), DeviceCredentialState.Retired);
        }

        await RunAsync();

        Assert.True(await ExistsAsync(profile.Id));
    }

    [Fact]
    public async Task A_profile_without_a_credential_counts_from_its_creation()
    {
        await using var db = fixture.CreateDbContext();
        var stale = await AddProfileAsync(db, ProfileKind.Provisional, Now.AddMonths(-13));
        var fresh = await AddProfileAsync(db, ProfileKind.Provisional, Now.AddMonths(-1));

        await RunAsync();

        Assert.False(await ExistsAsync(stale.Id));
        Assert.True(await ExistsAsync(fresh.Id));
    }

    [Fact]
    public async Task Registered_members_and_other_profile_kinds_are_never_touched()
    {
        await using var db = fixture.CreateDbContext();
        var kinds = new[] { ProfileKind.Member, ProfileKind.Archived, ProfileKind.System };
        var profiles = new List<Profile>();
        foreach (var kind in kinds)
        {
            var profile = await AddProfileAsync(db, kind, Now.AddYears(-5));
            profiles.Add(profile);
        }

        var keys = await AddContentAsync(profiles[0].Id);
        await AddCredentialAsync(db, profiles[0].Id, Now.AddYears(-5));

        await RunAsync();

        foreach (var profile in profiles)
        {
            Assert.True(await ExistsAsync(profile.Id));
        }

        Assert.True(await AnyRowOfAsync(profiles[0].Id));
        foreach (var key in keys)
        {
            Assert.True(await fixture.ObjectStore.ExistsAsync(key), key);
        }
    }

    [Fact]
    public async Task Content_authored_by_the_profile_does_not_block_the_removal()
    {
        var profile = await AddProvisionalAsync(Now.AddMonths(-13));
        var other = await AddProvisionalAsync(Now);
        long storyId;
        await using (var db = fixture.CreateDbContext())
        {
            // The database refuses such content for a provisional profile, so the profiles are members while it is written.
            await SetKindAsync(db, ProfileKind.Member, profile.Id, other.Id);
            var tank = new Tank { ProfileId = profile.Id, Title = "Tank", CreatedAt = Now };
            var story = new Story { AuthorProfileId = profile.Id, CreatedAt = Now, UpdatedAt = Now };
            db.Tanks.Add(tank);
            db.Stories.Add(story);
            await db.SaveChangesAsync();
            storyId = story.Id;
            db.Comments.Add(new Comment { StoryId = story.Id, AuthorProfileId = other.Id, Body = "x", CreatedAt = Now });
            db.Comments.Add(new Comment { TankId = tank.Id, AuthorProfileId = profile.Id, Body = "y", CreatedAt = Now });
            db.Follows.Add(new Follow { FollowerProfileId = other.Id, TargetProfileId = profile.Id, CreatedAt = Now });
            db.Collections.Add(new Collection { ProfileId = profile.Id, Title = "c", CreatedAt = Now });
            await db.SaveChangesAsync();
            await SetKindAsync(db, ProfileKind.Provisional, profile.Id, other.Id);
        }

        await RunAsync();

        Assert.False(await ExistsAsync(profile.Id));
        Assert.True(await ExistsAsync(other.Id));
        await using var check = fixture.CreateDbContext();
        Assert.False(await check.Stories.AnyAsync(s => s.Id == storyId));
    }

    [Fact]
    public async Task An_installation_that_signs_in_while_the_profile_is_removed_keeps_it()
    {
        var profile = await AddProvisionalAsync(Now.AddMonths(-13));
        Guid credentialId;
        await using (var db = fixture.CreateDbContext())
        {
            credentialId = await db.DeviceCredentials.Where(c => c.ProfileId == profile.Id).Select(c => c.Id).SingleAsync();
        }

        // The sign-in has updated its credential row and not yet committed when the pass reaches the profile.
        await using var signIn = fixture.CreateDbContext();
        await using var transaction = await signIn.Database.BeginTransactionAsync();
        await signIn.DeviceCredentials.Where(c => c.Id == credentialId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSignedRequestAt, Now));

        var pass = Task.Run(RunAsync);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(pass.IsCompleted);
        await transaction.CommitAsync();

        Assert.Equal(0, await pass);
        Assert.True(await ExistsAsync(profile.Id));
    }

    [Fact]
    public async Task A_sign_in_after_the_removal_finds_no_credential()
    {
        var profile = await AddProvisionalAsync(Now.AddMonths(-13));
        Guid credentialId;
        await using (var db = fixture.CreateDbContext())
        {
            credentialId = await db.DeviceCredentials.Where(c => c.ProfileId == profile.Id).Select(c => c.Id).SingleAsync();
        }

        await RunAsync();

        await using var late = fixture.CreateDbContext();
        var updated = await late.DeviceCredentials.Where(c => c.Id == credentialId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSignedRequestAt, Now));
        Assert.Equal(0, updated);
    }

    [Fact]
    public async Task A_profile_another_instance_is_processing_is_skipped_without_waiting()
    {
        var profile = await AddProvisionalAsync(Now.AddMonths(-13));
        await using var other = fixture.CreateDbContext();
        await using var transaction = await other.Database.BeginTransactionAsync();
        await other.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM public.profile WHERE id = {profile.Id} FOR UPDATE");

        var pass = Task.Run(RunAsync);
        var finished = await Task.WhenAny(pass, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(pass, finished);
        Assert.True(await ExistsAsync(profile.Id));

        await transaction.RollbackAsync();
        await RunAsync();
        Assert.False(await ExistsAsync(profile.Id));
    }

    [Fact]
    public async Task Stored_objects_are_deleted_after_the_commit()
    {
        var profile = await AddProvisionalAsync(Now.AddMonths(-13));
        var keys = await AddContentAsync(profile.Id);
        var profileGoneWhenObjectDeleted = new List<bool>();
        var store = new ObservedObjectStore(fixture.ObjectStore, async () =>
        {
            await using var db = fixture.CreateDbContext();
            profileGoneWhenObjectDeleted.Add(!await db.Profiles.AnyAsync(p => p.Id == profile.Id));
        });

        await RunWithAsync(store);

        Assert.NotEmpty(profileGoneWhenObjectDeleted);
        Assert.All(profileGoneWhenObjectDeleted, Assert.True);
        foreach (var key in keys)
        {
            Assert.False(await fixture.ObjectStore.ExistsAsync(key), key);
        }
    }

    [Fact]
    public async Task A_failing_object_delete_leaves_the_object_and_never_undoes_the_removal()
    {
        var profile = await AddProvisionalAsync(Now.AddMonths(-13));
        var keys = await AddContentAsync(profile.Id);
        var store = new ObservedObjectStore(fixture.ObjectStore, () => throw new InvalidOperationException("object store unavailable"));

        var removed = await RunWithAsync(store);

        Assert.True(removed >= 1);
        Assert.False(await ExistsAsync(profile.Id));
        Assert.False(await AnyRowOfAsync(profile.Id));
        foreach (var key in keys)
        {
            Assert.True(await fixture.ObjectStore.ExistsAsync(key), key);
        }
    }

    // Runs a pass whose object deletes go through the given store.
    private async Task<int> RunWithAsync(IObjectStore store)
    {
        using var scope = _host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<CichlidsDbContext>();
        var deleter = new StoredObjectDeleter(store, NullLogger<StoredObjectDeleter>.Instance);
        var videoRemoval = new VideoRemovalService(context, store, deleter, services.GetRequiredService<CareEntryMapper>(), _clock);
        var service = new ProvisionalAccountCleanupService(
            context, services.GetRequiredService<IOptions<ProvisionalAccountCleanupOptions>>(), deleter, videoRemoval, store, _clock,
            NullLogger<ProvisionalAccountCleanupService>.Instance);
        return await service.RunOnceAsync(CancellationToken.None);
    }

    // Reports every object delete to a callback before the delete runs; an exception of the callback fails the delete.
    private sealed class ObservedObjectStore(IObjectStore inner, Func<Task> onDelete) : IObjectStore
    {
        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            await onDelete();
            await inner.DeleteAsync(key, cancellationToken);
        }

        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            inner.PutAsync(key, content, contentType, cancellationToken);

        public Task PutAsync(string key, Stream content, string contentType, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken = default) =>
            inner.PutAsync(key, content, contentType, metadata, cancellationToken);

        public Task<ObjectHead?> HeadAsync(string key, CancellationToken cancellationToken = default) => inner.HeadAsync(key, cancellationToken);

        public Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default) => inner.GetAsync(key, cancellationToken);

        public Task<bool> DownloadAsync(string key, Stream destination, long maxBytes, CancellationToken cancellationToken = default) =>
            inner.DownloadAsync(key, destination, maxBytes, cancellationToken);

        public Task<string> BeginMultipartUploadAsync(string key, string contentType, CancellationToken cancellationToken = default) =>
            inner.BeginMultipartUploadAsync(key, contentType, cancellationToken);

        public Task<string> UploadPartAsync(string key, string uploadId, int partNumber, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default) =>
            inner.UploadPartAsync(key, uploadId, partNumber, content, cancellationToken);

        public Task CompleteMultipartUploadAsync(string key, string uploadId, IReadOnlyList<MultipartPart> parts, CancellationToken cancellationToken = default) =>
            inner.CompleteMultipartUploadAsync(key, uploadId, parts, cancellationToken);

        public Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken cancellationToken = default) =>
            inner.AbortMultipartUploadAsync(key, uploadId, cancellationToken);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);

        public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default) => inner.GetSizeAsync(key, cancellationToken);

        public IAsyncEnumerable<string> ListKeysAsync(string prefix, CancellationToken cancellationToken = default) =>
            inner.ListKeysAsync(prefix, cancellationToken);

        public string GetPublicUrl(string key) => inner.GetPublicUrl(key);
    }
}
