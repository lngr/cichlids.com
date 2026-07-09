using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Profiles;

/// <summary>
/// Query logic behind the public profile endpoint: only browsable member profiles are visible,
/// with the avatar resolved the same way as everywhere an author is shown and activity stats
/// counted only over that profile's public content.
/// </summary>
public sealed class ProfilesQueryService(CichlidsDbContext context, IObjectStore objectStore)
{
    public async Task<ProfileDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var profile = await context.Profiles
            .FirstOrDefaultAsync(p => p.Id == id && p.Kind == ProfileKind.Member, cancellationToken);

        if (profile is null)
        {
            return null;
        }

        var avatarThumbStorageKey = profile.AvatarMediaId is { } avatarMediaId
            ? await context.MediaVariants
                .Where(v => v.MediaItemId == avatarMediaId && v.Label == "thumb")
                .Select(v => v.StorageKey)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var avatarUrl = avatarThumbStorageKey is not null
            ? objectStore.GetPublicUrl(avatarThumbStorageKey)
            : profile.ExternalAvatarUrl;

        string? profileImageUrl = null;
        if (profile.ProfileImageMediaId is { } profileImageMediaId)
        {
            var storageKey = await context.MediaItems
                .Where(m => m.Id == profileImageMediaId)
                .Select(m => m.StorageKey)
                .FirstOrDefaultAsync(cancellationToken);

            if (storageKey is not null)
            {
                profileImageUrl = objectStore.GetPublicUrl(storageKey);
            }
        }

        var pictureCount = await context.Posts
            .CountAsync(p => p.AuthorProfileId == id && p.State == PostState.Published && p.DeletedAt == null, cancellationToken);

        var tankCount = await context.Tanks
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
            new ProfileStatsDto(pictureCount, tankCount, commentCount));
    }
}
