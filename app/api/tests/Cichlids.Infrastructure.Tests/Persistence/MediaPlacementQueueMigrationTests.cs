using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class MediaPlacementQueueMigrationTests(PostgresFixture fixture)
{
    private const string PreviousMigration = "20261016155023_MediaLinks";

    [Fact]
    public async Task GivenStoredMedia_WhenTheQueueColumnsAreAdded_ThenPlacedPublicFollowsTheVariantKeys()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO public.media_item (id, kind, storage_key, created_at) VALUES
                (1, 'photo', 'originals/a.jpg', now()), (2, 'photo', 'private/care/1/b.jpg', now()),
                (3, 'video', 'private/video/1/c.mp4', now()), (4, 'video', 'private/video/1/d.mp4', now()),
                (5, 'photo', 'originals/e.jpg', now());
            INSERT INTO public.media_variant (media_item_id, label, storage_key) VALUES
                (1, 'thumb', 'variants/thumb/originals/a.jpg.jpg'), (2, 'thumb', 'private/variants/thumb/private/care/1/b.jpg.jpg'),
                (3, 'hls', 'variants/video/1/c/master.m3u8');
            INSERT INTO public.video_job (media_item_id, status, is_private, attempts, run_after, created_at)
                VALUES (4, 'queued', true, 0, now(), now());
            """);

        await migrator.MigrateAsync();

        var placed = await context.MediaItems.AsNoTracking().OrderBy(m => m.Id).Select(m => new { m.Id, m.PlacedPublic, m.PlacementPending }).ToListAsync();
        Assert.Equal([true, false, true, false, true], placed.Select(m => m.PlacedPublic));
        Assert.All(placed, m => Assert.False(m.PlacementPending));
    }
}
