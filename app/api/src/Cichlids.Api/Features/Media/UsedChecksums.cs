using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Media;

/// <summary>
/// Reads and writes the checksums of the photos a member used as proof. A photo counts as used before
/// when its checksum is recorded for the member, whatever happened to its links or its file since.
/// </summary>
public static class UsedChecksums
{
    /// <summary>
    /// The checksums among the given ones that the member used before in the given way.
    /// </summary>
    public static async Task<HashSet<string>> UsedAmongAsync(
        this CichlidsDbContext db, long profileId, UsedChecksumKind kind, IReadOnlyCollection<string> checksums, CancellationToken cancellationToken)
    {
        if (checksums.Count == 0)
        {
            return [];
        }

        var used = await db.UsedChecksums.AsNoTracking()
            .Where(u => u.ProfileId == profileId && u.Kind == kind && checksums.Contains(u.ChecksumSha256))
            .Select(u => u.ChecksumSha256).ToListAsync(cancellationToken);
        return [.. used];
    }

    /// <summary>
    /// Records the checksums as used by the member in the given way. A checksum that is recorded already
    /// keeps its first use. Photos without a checksum are skipped.
    /// </summary>
    public static async Task RecordUsedAsync(
        this CichlidsDbContext db, long profileId, UsedChecksumKind kind, IEnumerable<string?> checksums, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var name = SnakeCaseEnum.ToSnakeCase(kind);
        foreach (var checksum in checksums.OfType<string>().Distinct())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public.used_checksum (profile_id, checksum_sha256, kind, first_used_at)
                VALUES ({profileId}, {checksum}, {name}, {now})
                ON CONFLICT DO NOTHING
                """,
                cancellationToken);
        }
    }
}
