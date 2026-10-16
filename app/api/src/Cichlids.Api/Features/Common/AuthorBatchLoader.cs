using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Common;

/// <summary>
/// Loads the AuthorRow for a batch of profile ids in one query, so a page of list
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
                context.MediaVariants
                    .Where(v => v.Label == "thumb"
                        && context.MediaLinks.Any(l => l.ProfileId == pr.Id && l.Role == MediaLinkRole.Avatar && l.MediaItemId == v.MediaItemId))
                    .Select(v => v.StorageKey)
                    .FirstOrDefault(),
                context.MediaVariants
                    .Where(v => v.Label == MediaVariantSpec.PublicCopyLabel
                        && context.MediaLinks.Any(l => l.ProfileId == pr.Id && l.Role == MediaLinkRole.Avatar && l.MediaItemId == v.MediaItemId))
                    .Select(v => v.StorageKey)
                    .FirstOrDefault(),
                pr.ExternalAvatarUrl))
            .ToDictionaryAsync(a => a.Id, cancellationToken);
}
