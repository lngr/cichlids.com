using Cichlids.Api.Features.Retrospectives;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// Tells whether the archive retrospective of a member is due. A due archive retrospective takes
/// precedence over the return retrospective.
/// </summary>
public interface IArchiveRetrospectiveGate
{
    Task<bool> IsDueAsync(long profileId, CancellationToken cancellationToken);
}

/// <summary>
/// The archive retrospective of a member is due while the member is a legacy member (profile.legacy_id), has
/// archive content and has not seen the retrospective.
/// </summary>
public sealed class ArchiveRetrospectiveGate(CichlidsDbContext context, ArchiveRetrospectiveQueryService archive) : IArchiveRetrospectiveGate
{
    public async Task<bool> IsDueAsync(long profileId, CancellationToken cancellationToken) =>
        await context.Profiles.AsNoTracking().AnyAsync(p => p.Id == profileId && p.LegacyId != null && p.ArchiveRetrospectiveSeenAt == null, cancellationToken)
        && await archive.ExistsAsync(profileId, cancellationToken);
}
