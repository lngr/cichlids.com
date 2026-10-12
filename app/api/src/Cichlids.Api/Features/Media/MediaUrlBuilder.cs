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

    public ImageUrlsDto Build(IReadOnlyDictionary<string, string> variantStorageKeysByLabel) =>
        new(
            FindUrl(variantStorageKeysByLabel, "thumb"),
            FindUrl(variantStorageKeysByLabel, "small"),
            FindUrl(variantStorageKeysByLabel, "medium"),
            FindUrl(variantStorageKeysByLabel, "large"));

    /// <summary>
    /// The URL of the largest variant present, or null when the media item has no variant.
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

        return null;
    }

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
