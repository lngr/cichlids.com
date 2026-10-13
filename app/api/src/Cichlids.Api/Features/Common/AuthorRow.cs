using Cichlids.Api.Features.Media;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Features.Common;

/// <summary>
/// The raw fields a query projects for an author before the object store resolves the avatar
/// storage keys (if any) to a public URL. Kept as its own shape because that resolution cannot run
/// inside the SQL translation of the surrounding query. The avatar is the thumbnail of the avatar
/// photo, otherwise its public copy, otherwise the external avatar URL.
/// </summary>
public sealed record AuthorRow(
    long Id, string Username, string? DisplayName, string? AvatarThumbStorageKey, string? AvatarCopyStorageKey, string? ExternalAvatarUrl)
{
    public AuthorDto ToDto(IObjectStore objectStore) => new(
        Id,
        Username,
        DisplayName,
        ResolveAvatarUrl(objectStore, AvatarThumbStorageKey, AvatarCopyStorageKey, ExternalAvatarUrl));

    /// <summary>
    /// The public URL of an avatar photo's thumbnail, otherwise of its public copy, otherwise the
    /// external avatar URL, or null when there is none.
    /// </summary>
    public static string? ResolveAvatarUrl(
        IObjectStore objectStore, string? thumbStorageKey, string? copyStorageKey, string? externalAvatarUrl) =>
        new MediaUrlBuilder(objectStore).BuildThumbnail(thumbStorageKey, copyStorageKey) ?? externalAvatarUrl;
}
