using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Cichlids.Api.Tests.Features.Discover;

public class DiscoverArchiveScaleTests(ITestOutputHelper output)
{
    [ArchiveScaleFact]
    public async Task AnonymousDiscover_ReportsColdWarmAndDifferentRegionDurations()
    {
        await using var lifetime = new FixtureLifetime();
        await lifetime.Fixture.InitializeAsync();
        var fixture = lifetime.Fixture;
        fixture.Client.Timeout = TimeSpan.FromMinutes(5);
        await using var db = fixture.CreateDbContext();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE profile, media_item, species RESTART IDENTITY CASCADE;
            INSERT INTO profile (id, username, kind, created_at)
            VALUES (1, 'synthetic-member', 'member', now());
            INSERT INTO tank (id, profile_id, title, region, state, created_at)
            SELECT n, 1, 'Synthetic tank ' || n,
                (ARRAY['lake_malawi','lake_tanganyika','lake_victoria','africa_other','central_america','south_america','mixed'])[1 + n % 7],
                'published', now() FROM generate_series(1, 5000) n;
            INSERT INTO media_item (id, kind, storage_key, created_at)
            SELECT n, 'photo', 'synthetic/' || n, '2020-01-01'::timestamptz + (n % 40) * interval '1 month'
            FROM generate_series(1, 50000) n;
            INSERT INTO media_variant (media_item_id, label, storage_key)
            SELECT n, 'large', 'variants/large/synthetic/' || n FROM generate_series(1, 50000) n;
            INSERT INTO post (id, author_profile_id, tank_id, title, kind, topic, state, created_at, updated_at)
            SELECT n, 1,
                CASE WHEN n <= 2800 AND (n-1) % 40 < 8 + ((n-1) / 40) % 33 THEN 1 + (n-1) / 40 END,
                'Synthetic picture ' || n, 'single', 'tanks', 'published',
                '2020-01-01'::timestamptz + (n % 40) * interval '1 month', now()
            FROM generate_series(1, 50000) n;
            INSERT INTO post_media (post_id, media_item_id) SELECT n, n FROM generate_series(1, 50000) n;
            INSERT INTO rating (tank_id, stars, created_at)
            SELECT 1 + (n-1) % 5000, (1 + n % 5)::smallint, now() FROM generate_series(1, 7570) n;
            INSERT INTO rating (post_id, stars, created_at)
            SELECT 1 + (n-1) % 50000, (1 + n % 5)::smallint, now() FROM generate_series(1, 1292430) n;
            ANALYZE;
            """);
        Assert.Equal(5000, await db.Tanks.CountAsync());
        Assert.Equal(50000, await db.Posts.CountAsync());
        Assert.Equal(1300000, await db.Ratings.CountAsync());
        Assert.Equal(70, await db.Posts.Where(p => p.TankId != null).GroupBy(p => p.TankId).CountAsync());
        foreach (var (label, path) in new[]
        {
            ("cold", "/api/discover"),
            ("warm", "/api/discover"),
            ("different-region", "/api/discover?regions=lake_malawi"),
            ("another-region", "/api/discover?regions=lake_tanganyika"),
        })
        {
            var watch = Stopwatch.StartNew();
            using var response = await fixture.Client.GetAsync(path);
            var body = await response.Content.ReadAsByteArrayAsync();
            watch.Stop();
            response.EnsureSuccessStatusCode();
            output.WriteLine($"DISCOVER {label}: {watch.Elapsed.TotalMilliseconds:F1} ms, {body.Length} bytes");
        }
    }

    private sealed class FixtureLifetime : IAsyncDisposable
    {
        public ApiFixture Fixture { get; } = new();
        public async ValueTask DisposeAsync() => await Fixture.DisposeAsync();
    }
}

public sealed class ArchiveScaleFactAttribute : FactAttribute
{
    public ArchiveScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CICHLIDS_DISCOVER_BENCHMARK") != "1")
            Skip = "Opt in with CICHLIDS_DISCOVER_BENCHMARK=1 and --filter FullyQualifiedName~DiscoverArchiveScaleTests; uses a disposable PostgreSQL container.";
    }
}
