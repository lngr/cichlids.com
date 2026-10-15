using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

public static class CareTanks
{
    /// <summary>
    /// Locks the tank row for the rest of the transaction and tells whether the profile owns the
    /// live tank, so concurrent care writes of one tank run one after the other.
    /// </summary>
    public static async Task<bool> LockOwnAsync(CichlidsDbContext context, long tankId, long profileId, CancellationToken ct)
    {
        var tank = await context.Tanks.FromSqlInterpolated($"SELECT * FROM public.tank WHERE id = {tankId} FOR UPDATE").SingleOrDefaultAsync(ct);
        return tank is not null && tank.ProfileId == profileId && tank.DeletedAt is null;
    }
}
