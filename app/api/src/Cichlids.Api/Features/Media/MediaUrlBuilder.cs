using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Features.Media;

/// <summary>
/// Builds public media URLs from a media item's variant storage keys, resolving every key through
/// the configured object store so callers never see storage keys directly. Only keys under the
/// public prefixes of the object store resolve; any other key, such as an original's, throws, so
/// a private object never reaches a response.
/// </summary>
public sealed class MediaUrlBuilder(IObjectStore objectStore)
{
    private static readonly string[] LabelsLargestFirst = ["large", "medium", "small", "thumb"];

    /// <summary>
    /// The URLs of the sized variants. A media item whose original cannot be decoded has a public
    /// copy and no sized variant; every size resolves to that copy.
    /// </summary>
    public ImageUrlsDto Build(IReadOnlyDictionary<string, string> variantStorageKeysByLabel)
    {
        if (!LabelsLargestFirst.Any(variantStorageKeysByLabel.ContainsKey)
            && FindUrl(variantStorageKeysByLabel, MediaVariantSpec.PublicCopyLabel) is { } copy)
        {
            return new ImageUrlsDto(copy, copy, copy, copy);
        }

        return new ImageUrlsDto(
            FindUrl(variantStorageKeysByLabel, "thumb"),
            FindUrl(variantStorageKeysByLabel, "small"),
            FindUrl(variantStorageKeysByLabel, "medium"),
            FindUrl(variantStorageKeysByLabel, "large"));
    }

    /// <summary>
    /// The URL of the largest sized variant present, otherwise of the public copy, or null when
    /// the media item has neither.
    /// </summary>
    public string? BuildBest(IReadOnlyDictionary<string, string> variantStorageKeysByLabel)
    {
        foreach (var label in LabelsLargestFirst)
        {
            if (FindUrl(variantStorageKeysByLabel, label) is { } url)
            {
                return url;
            }
        }

        return FindUrl(variantStorageKeysByLabel, MediaVariantSpec.PublicCopyLabel);
    }

    /// <summary>
    /// The URL of the thumbnail, otherwise of the public copy, or null when neither key is given.
    /// </summary>
    public string? BuildThumbnail(string? thumbStorageKey, string? copyStorageKey) =>
        thumbStorageKey is not null ? PublicUrl(thumbStorageKey)
        : copyStorageKey is not null ? PublicUrl(copyStorageKey)
        : null;

    private string? FindUrl(IReadOnlyDictionary<string, string> variantStorageKeysByLabel, string label) =>
        variantStorageKeysByLabel.TryGetValue(label, out var storageKey)
            ? PublicUrl(storageKey)
            : null;

    private string PublicUrl(string storageKey)
    {
        if (!MediaStorageKeys.PublicPrefixes.Any(prefix => storageKey.StartsWith(prefix, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Storage key '{storageKey}' is outside the public prefixes and has no public URL.");
        }

        return objectStore.GetPublicUrl(storageKey);
    }
}
