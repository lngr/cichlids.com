using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Features.Media;

/// <summary>
/// Builds the public <see cref="ImageUrlsDto"/> for a media item from its original storage key
/// and whichever of its variant renditions exist, resolving every key through the configured
/// object store so callers never see storage keys directly.
/// </summary>
public sealed class MediaUrlBuilder(IObjectStore objectStore)
{
    public ImageUrlsDto Build(string originalStorageKey, IReadOnlyDictionary<string, string> variantStorageKeysByLabel) =>
        new(
            FindUrl(variantStorageKeysByLabel, "thumb"),
            FindUrl(variantStorageKeysByLabel, "small"),
            FindUrl(variantStorageKeysByLabel, "medium"),
            FindUrl(variantStorageKeysByLabel, "large"),
            objectStore.GetPublicUrl(originalStorageKey));

    private string? FindUrl(IReadOnlyDictionary<string, string> variantStorageKeysByLabel, string label) =>
        variantStorageKeysByLabel.TryGetValue(label, out var storageKey)
            ? objectStore.GetPublicUrl(storageKey)
            : null;
}
