using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Common;

/// <summary>
/// Loads the <see cref="AuthorRow"/> for a batch of profile ids in one query, so a page of list
/// items can resolve every author it needs without a round trip per item.
/// </summary>
public static class AuthorBatchLoader
{
    public static Task<Dictionary<long, AuthorRow>> LoadAsync(
        CichlidsDbContext context, IReadOnlyCollection<long> profileIds, CancellationToken cancellationToken) =>
        context.Profiles
            .Where(pr => profileIds.Contains(pr.Id))
            .Select(pr => new AuthorRow(
                pr.Id,
                pr.Username,
                pr.DisplayName,
                pr.AvatarMediaId != null
                    ? context.MediaVariants
                        .Where(v => v.MediaItemId == pr.AvatarMediaId && v.Label == "thumb")
                        .Select(v => v.StorageKey)
                        .FirstOrDefault()
                    : null,
                pr.ExternalAvatarUrl))
            .ToDictionaryAsync(a => a.Id, cancellationToken);
}
