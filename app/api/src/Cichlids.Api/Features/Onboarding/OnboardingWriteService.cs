using System.Text.Json;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Onboarding;

public sealed class OnboardingWriteService(CichlidsDbContext context, TanksWriteService tanks)
{
    public static (string Code, string Detail)? Validate(OnboardingRequest request)
    {
        var paths = (request.Tank is null ? 0 : 1) + (request.LegacyTankId is null ? 0 : 1)
            + (request.Interests is null ? 0 : 1) + (request.Skip ? 1 : 0);
        if (paths != 1) return ("onboarding_invalid", "Choose one onboarding path.");
        if (request.Tank is { } tank)
        {
            if (TanksWriteService.Validate(tank) is { } error) return error;
            if (tank.Region is null) return ("tank_region_invalid", "Choose a region.");
        }
        if (request.Interests is { } interests && !ValidInterests(interests))
            return ("interests_invalid", "Choose regions from the interest list.");
        if (request.Region is { } region && !TankRegions.IsSelectable(region))
            return ("tank_region_invalid", "Choose a region.");
        return null;
    }

    public static bool ValidInterests(string[]? interests) => interests is not null && interests.Length <= 32 && interests.All(TankRegions.IsInterest);

    public async Task<(OnboardingResultDto? Result, string? Error)> CompleteAsync(
        long profileId, OnboardingRequest request, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var profile = await context.Profiles.FromSqlInterpolated($"SELECT * FROM public.profile WHERE id = {profileId} FOR UPDATE").SingleAsync(ct);
        await context.Entry(profile).ReloadAsync(ct);
        Tank? tank = null;
        string? badge = null;
        if (request.LegacyTankId is { } id)
        {
            tank = await context.Tanks.SingleOrDefaultAsync(t => t.Id == id && t.ProfileId == profileId && t.LegacyId != null
                && t.State == TankState.Published && t.DeletedAt == null, ct);
            if (tank is null) return (null, "legacy_tank_not_found");
            var region = tank.Region ?? TankRegions.FromLegacyCategory(tank.Category);
            if (region == TankRegion.AmericasUnassigned && request.Region is not (TankRegion.CentralAmerica or TankRegion.SouthAmerica))
                return (null, "tank_region_invalid");
            tank.Region = region == TankRegion.AmericasUnassigned || region is null ? request.Region : region;
            tank.AdoptedAt ??= DateTimeOffset.UtcNow;
            badge = "returned";
            AddEvent("tank.adopted", "tank", tank.Id, new { tankId = tank.Id, profileId });
        }
        else if (request.Tank is { } create)
        {
            tank = await tanks.CreateAsync(create, profileId, ct);
            badge = profile.LegacyId is not null ? "returned" : "first_tank";
        }
        if (tank?.Region is { } tankRegion && TankRegions.IsInterest(TankRegions.Key(tankRegion)))
            profile.Interests = profile.Interests.Append(TankRegions.Key(tankRegion)).Distinct().Order().ToArray();
        if (request.Interests is { } interests) profile.Interests = interests.Distinct().Order().ToArray();
        if (badge is not null) profile.Badges = profile.Badges.Append(badge).Distinct().ToArray();
        profile.OnboardingCompletedAt ??= DateTimeOffset.UtcNow;
        AddEvent("profile.onboarding_completed", "profile", profileId, new { profileId, tankId = tank?.Id, badge, skipped = request.Skip });
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (new OnboardingResultDto(tank?.Id, tank?.Title, badge), null);
    }

    public async Task<string[]> SetInterestsAsync(long profileId, string[] interests, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var profile = await context.Profiles.FromSqlInterpolated($"SELECT * FROM public.profile WHERE id = {profileId} FOR UPDATE").SingleAsync(ct);
        profile.Interests = interests.Distinct().Order().ToArray();
        AddEvent("profile.interests_updated", "profile", profileId, new { profileId, interests = profile.Interests });
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return profile.Interests;
    }

    public async Task<string?> RefineAsync(long profileId, long id, TankRegion region, CancellationToken ct)
    {
        if (region is not (TankRegion.CentralAmerica or TankRegion.SouthAmerica)) return "tank_region_invalid";
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var profile = await context.Profiles.FromSqlInterpolated($"SELECT * FROM public.profile WHERE id = {profileId} FOR UPDATE").SingleAsync(ct);
        await context.Entry(profile).ReloadAsync(ct);
        var tank = await context.Tanks.FromSqlInterpolated($"SELECT * FROM public.tank WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (tank is null || tank.ProfileId != profileId || tank.DeletedAt != null) return "legacy_tank_not_found";
        if (tank.Region != TankRegion.AmericasUnassigned) return "tank_region_invalid";
        tank.Region = region;
        profile.Interests = profile.Interests.Append(TankRegions.Key(region)).Distinct().Order().ToArray();
        AddEvent("profile.interests_updated", "profile", profileId, new { profileId, interests = profile.Interests });
        AddEvent("tank.region_refined", "tank", id, new { tankId = id, profileId, region = TankRegions.Key(region) });
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    private void AddEvent(string type, string aggregate, long id, object payload) => context.OutboxEvents.Add(
        OutboxEvent.Create(type, aggregate, id.ToString(), JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)), DateTimeOffset.UtcNow));
}
