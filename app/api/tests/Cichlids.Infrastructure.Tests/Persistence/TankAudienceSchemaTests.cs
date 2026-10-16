using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Infrastructure.Tests.Persistence;

/// <summary>
/// The constraints of follow requests and selected members: a known status, a pending status only for the
/// follow of a tank, one relation per member and tank, and selected members that go with their tank.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TankAudienceSchemaTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task AFollowIsApprovedByDefaultAndKnowsOnlyPendingAndApproved()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var member = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        context.Follows.Add(new Follow { FollowerProfileId = member.Id, TargetTankId = tank.Id, CreatedAt = Now });
        await context.SaveChangesAsync();
        Assert.Equal(FollowStatus.Approved, (await context.Follows.AsNoTracking().SingleAsync(f => f.FollowerProfileId == member.Id)).Status);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.follow SET status = 'declined' WHERE follower_profile_id = {member.Id}"));
        Assert.Equal("ck_follow_status", exception.ConstraintName);
    }

    [Fact]
    public async Task AFollowOfAProfileCannotBePending()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var member = await TestDataBuilder.CreateProfileAsync(context);
        context.Follows.Add(new Follow { FollowerProfileId = member.Id, TargetProfileId = owner.Id, Status = FollowStatus.Pending, CreatedAt = Now });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal("ck_follow_pending_tank", Assert.IsType<PostgresException>(exception.InnerException).ConstraintName);
    }

    [Fact]
    public async Task AMemberIsSelectedOncePerTankAndTheSelectionGoesWithTheTank()
    {
        await using var context = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(context);
        var member = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, owner.Id);
        context.TankSelectedMembers.Add(new TankSelectedMember { TankId = tank.Id, MemberProfileId = member.Id, AddedAt = Now });
        await context.SaveChangesAsync();

        context.TankSelectedMembers.Add(new TankSelectedMember { TankId = tank.Id, MemberProfileId = member.Id, AddedAt = Now });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal("ix_tank_selected_member_tank_id_member_profile_id", Assert.IsType<PostgresException>(exception.InnerException).ConstraintName);
        context.ChangeTracker.Clear();

        await context.Tanks.Where(t => t.Id == tank.Id).ExecuteDeleteAsync();
        Assert.Empty(await context.TankSelectedMembers.Where(s => s.TankId == tank.Id).ToListAsync());
    }
}
