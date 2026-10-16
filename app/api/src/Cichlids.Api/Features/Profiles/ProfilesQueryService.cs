using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Profiles;

/// <summary>
/// Query logic behind the public profile endpoint: only browsable member profiles are visible,
/// with the avatar resolved the same way as everywhere an author is shown, the profile image
/// resolved to its largest variant, and activity stats counted only over that profile's public
/// content.
/// </summary>
public sealed class ProfilesQueryService(CichlidsDbContext context, IObjectStore objectStore)
{
    public async Task<ProfileDetailDto?> GetByIdAsync(long id, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var profile = await context.Profiles
            .FirstOrDefaultAsync(p => p.Id == id && p.Kind == ProfileKind.Member, cancellationToken);

        if (profile is null)
        {
            return null;
        }

        var avatarKeysByLabel = profile.AvatarMediaId is { } avatarMediaId
            ? await context.MediaVariants
                .Where(v => v.MediaItemId == avatarMediaId && (v.Label == "thumb" || v.Label == MediaVariantSpec.PublicCopyLabel))
                .ToDictionaryAsync(v => v.Label, v => v.StorageKey, cancellationToken)
            : [];

        var avatarUrl = AuthorRow.ResolveAvatarUrl(
            objectStore,
            avatarKeysByLabel.GetValueOrDefault("thumb"),
            avatarKeysByLabel.GetValueOrDefault(MediaVariantSpec.PublicCopyLabel),
            profile.ExternalAvatarUrl);

        string? profileImageUrl = null;
        if (profile.ProfileImageMediaId is { } profileImageMediaId)
        {
            var variantStorageKeysByLabel = await context.MediaVariants
                .Where(v => v.MediaItemId == profileImageMediaId)
                .ToDictionaryAsync(v => v.Label, v => v.StorageKey, cancellationToken);

            profileImageUrl = new MediaUrlBuilder(objectStore).BuildBest(variantStorageKeysByLabel);
        }

        var pictureCount = await context.Posts
            .CountAsync(p => p.AuthorProfileId == id && p.State == PostState.Published && p.DeletedAt == null, cancellationToken);

        var tankCount = await context.Tanks.Where(TankVisibilityRules.ProfileVisibleTo(viewerProfileId))
            .CountAsync(t => t.ProfileId == id && t.State == TankState.Published && t.DeletedAt == null, cancellationToken);

        var commentCount = await context.Comments
            .CountAsync(c => c.AuthorProfileId == id && c.DeletedAt == null, cancellationToken);

        return new ProfileDetailDto(
            profile.Id,
            profile.Username,
            profile.DisplayName,
            profile.City,
            profile.CountryCode,
            profile.CreatedAt,
            avatarUrl,
            profileImageUrl,
            new ProfileStatsDto(pictureCount, tankCount, commentCount),
            profile.OnboardingCompletedAt, profile.Interests, profile.Badges);
    }
}
