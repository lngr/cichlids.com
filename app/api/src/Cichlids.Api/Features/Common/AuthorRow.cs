using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Features.Common;

/// <summary>
/// The raw fields a query projects for an author before the object store resolves the avatar
/// storage key (if any) to a public URL. Kept as its own shape because that resolution cannot run
/// inside the SQL translation of the surrounding query.
/// </summary>
public sealed record AuthorRow(long Id, string Username, string? DisplayName, string? AvatarThumbStorageKey, string? ExternalAvatarUrl)
{
    public AuthorDto ToDto(IObjectStore objectStore) => new(
        Id,
        Username,
        DisplayName,
        AvatarThumbStorageKey is not null ? objectStore.GetPublicUrl(AvatarThumbStorageKey) : ExternalAvatarUrl);
}
