// Story: TASK-5.20.4
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class UsedChecksumMigrationTests(PostgresFixture fixture)
{
    private const string PreviousMigration = "20261016181720_GalleryStories";

    [Fact]
    public async Task GivenPhotosThatServeAsProof_WhenTheTableIsAdded_ThenTheirChecksumsAreRecordedPerMemberAndKind()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO public.profile (id, username, kind, created_at) VALUES (1, 'owner', 'member', now()), (2, 'other', 'member', now());
            INSERT INTO public.tank (id, profile_id, title, state, created_at) VALUES (1, 1, 'Tank', 'published', now());
            INSERT INTO public.media_item (id, owner_profile_id, kind, storage_key, checksum_sha256, created_at) VALUES
                (10, 1, 'photo', 'k10', 'sum-finished', now()), (11, 1, 'photo', 'k11', 'sum-open', now()),
                (12, 1, 'photo', 'k12', 'sum-care', now()), (13, 1, 'photo', 'k13', 'sum-proof', now()),
                (15, 1, 'photo', 'k15', NULL, now()),
                (16, 2, 'photo', 'k16', 'sum-care', now());
            INSERT INTO public.water_change_session (id, tank_id, profile_id, started_at, finished_at, duration_seconds) VALUES
                (1, 1, 1, now(), now(), 600), (2, 1, 1, now(), NULL, NULL);
            INSERT INTO public.care_entry (id, tank_id, author_profile_id, kind, occurred_at, recorded_at) VALUES (1, 1, 1, 'water_change', now(), now());
            INSERT INTO public.media_link (media_item_id, context_type, role, position, water_change_session_id) VALUES
                (10, 'water_change_session', 'evidence', 0, 1), (11, 'water_change_session', 'evidence', 0, 2);
            INSERT INTO public.media_link (media_item_id, context_type, role, position, care_entry_id) VALUES
                (12, 'care_entry', 'photo', 0, 1), (15, 'care_entry', 'photo', 1, 1);
            INSERT INTO public.media_link (media_item_id, context_type, role, position, tank_id) VALUES (13, 'tank', 'decommission', 0, 1);
            """);

        await migrator.MigrateAsync();

        var rows = await context.UsedChecksums.AsNoTracking().OrderBy(u => u.Kind).ThenBy(u => u.ChecksumSha256).ToListAsync();
        Assert.Equal(
            [(UsedChecksumKind.Proof, "sum-care"), (UsedChecksumKind.Proof, "sum-proof"), (UsedChecksumKind.WaterChangeEvidence, "sum-finished")],
            rows.Select(u => (u.Kind, u.ChecksumSha256)));
        Assert.All(rows, u => Assert.Equal(1L, u.ProfileId));
    }

    [Fact]
    public async Task GivenUsedChecksums_ThenOnlyKnownKindsAreAcceptedAndTheyGoWithTheMember()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO public.profile (id, username, kind, created_at) VALUES (1, 'owner', 'member', now());
            INSERT INTO public.used_checksum (profile_id, checksum_sha256, kind) VALUES (1, 'a', 'proof'), (1, 'a', 'water_change_evidence');
            """);

        Assert.Equal(2, await context.UsedChecksums.CountAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO public.used_checksum (profile_id, checksum_sha256, kind) VALUES (1, 'b', 'other')"));
        await context.Database.ExecuteSqlRawAsync("DELETE FROM public.profile WHERE id = 1");

        Assert.Equal(0, await context.UsedChecksums.CountAsync());
    }
}
