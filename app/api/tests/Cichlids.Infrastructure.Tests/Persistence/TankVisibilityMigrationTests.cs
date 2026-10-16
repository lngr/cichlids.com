using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class TankVisibilityMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ExistingTanksStayPublicWithPlacedMediaAndWithoutConsent()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261016071256_WaterChangeSessionPhotos");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO public.profile (id, username, kind, created_at) VALUES (1, 'before', 'member', now());
            INSERT INTO public.tank (id, profile_id, title, state, created_at) VALUES (1, 1, 'Archive tank', 'published', now());
            INSERT INTO public.tank (id, profile_id, title, state, created_at) VALUES (2, 1, 'Tank before the question', 'published', now());
            INSERT INTO public.care_entry (id, tank_id, author_profile_id, kind, occurred_at, recorded_at) VALUES (1, 1, 1, 'water_change', now(), now());
            """);

        await migrator.MigrateAsync();

        var tanks = await context.Tanks.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, tanks.Count);
        Assert.All(tanks, t =>
        {
            Assert.Equal(TankVisibility.Public, t.ProfileVisibility);
            Assert.Equal(TankVisibility.Public, t.StreamVisibility);
        });
        Assert.Empty(await context.TankVisibilityConsents.ToListAsync());
        Assert.False((await context.CareEntries.AsNoTracking().SingleAsync()).OnlyMe);
    }

    [Theory]
    [InlineData("followers", "private", "ck_tank_profile_visibility")]
    [InlineData("public", "selected_members", "ck_tank_stream_visibility")]
    [InlineData("private", "public", "ck_tank_stream_within_profile")]
    public async Task TheDatabaseRefusesUnknownLevelsAndAStreamWiderThanTheProfile(string profile, string stream, string constraint)
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.tank (profile_id, title, state, created_at, profile_visibility, stream_visibility)
            VALUES ({owner.Id}, 'Refused', 'published', now(), {profile}, {stream})
            """));
        Assert.Equal(constraint, exception.ConstraintName);
    }

    [Theory]
    [InlineData("public", "public")]
    [InlineData("public", "private")]
    [InlineData("private", "private")]
    public async Task TheDatabaseAcceptsEveryLevelPairWithAStreamWithinTheProfile(string profile, string stream)
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var affected = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.tank (profile_id, title, state, created_at, profile_visibility, stream_visibility)
            VALUES ({owner.Id}, 'Accepted', 'published', now(), {profile}, {stream})
            """);
        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task AConsentNeedsAKnownContextAndGoesWithItsTank()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        context.TankVisibilityConsents.Add(new TankVisibilityConsent
        {
            TankId = tank.Id, Answer = true, AnsweredAt = DateTimeOffset.UtcNow, Context = "elsewhere",
        });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal("ck_tank_visibility_consent_context", Assert.IsType<PostgresException>(exception.InnerException).ConstraintName);
        context.ChangeTracker.Clear();

        context.TankVisibilityConsents.Add(new TankVisibilityConsent
        {
            TankId = tank.Id, Answer = true, AnsweredAt = DateTimeOffset.UtcNow, Context = TankVisibilityConsent.CreationContext,
        });
        await context.SaveChangesAsync();
        await context.Tanks.Where(t => t.Id == tank.Id).ExecuteDeleteAsync();
        Assert.Empty(await context.TankVisibilityConsents.Where(c => c.TankId == tank.Id).ToListAsync());
    }
}
