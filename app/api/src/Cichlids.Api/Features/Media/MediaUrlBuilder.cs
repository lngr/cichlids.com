using System.Text.RegularExpressions;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Features.Media;

/// <summary>
/// Builds public media URLs from a media item's variant storage keys, resolving every key through
/// the configured object store so callers never see storage keys directly. Only keys under the
/// public prefixes of the object store resolve; any other key, such as an original's, throws, so
/// a private object never reaches a response.
/// </summary>
public sealed class MediaUrlBuilder(IObjectStore objectStore, TankMediaUrlSigner? privateSigner = null)
{
    private const string PrivatePrefix = "private/";

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
    /// The URLs of the sized variants of a media item whose variants may live under the private
    /// prefix, as the media of a tank do while its placement follows a change of visibility. A
    /// variant under a public prefix resolves to its public URL. A variant under the private prefix
    /// resolves to a signed path when signed paths are allowed and the builder has a signer, and to
    /// null otherwise. Callers allow signed paths only for a viewer who sees the tank, and never for
    /// a response that is cached across viewers. The kind of URL follows the tank, not the place of the
    /// object: for media of a tank that is not public (forcePrivate) no variant resolves to a public URL,
    /// even while its object still lies under the public prefix.
    /// </summary>
    public ImageUrlsDto Build(IEnumerable<MediaVariant> variants, bool allowSigned, bool forcePrivate = false)
    {
        var byLabel = variants.ToDictionary(v => v.Label);
        string? ByLabel(string label) => byLabel.TryGetValue(label, out var variant) ? UrlOf(variant, allowSigned, forcePrivate) : null;
        if (!LabelsLargestFirst.Any(byLabel.ContainsKey) && ByLabel(MediaVariantSpec.PublicCopyLabel) is { } copy)
        {
            return new ImageUrlsDto(copy, copy, copy, copy);
        }

        return new ImageUrlsDto(ByLabel("thumb"), ByLabel("small"), ByLabel("medium"), ByLabel("large"));
    }

    private string? UrlOf(MediaVariant variant, bool allowSigned, bool forcePrivate) =>
        forcePrivate || variant.StorageKey.StartsWith(PrivatePrefix, StringComparison.Ordinal)
            ? allowSigned && privateSigner is not null ? privateSigner.PhotoPathFor(variant.Id) : null
            : PublicUrl(variant.StorageKey);

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
        if (!MediaStorageKeys.PublicPrefixes.Any(prefix => storageKey.StartsWith(prefix, StringComparison.Ordinal))
            && !Regex.IsMatch(storageKey, @"\Agolden/[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}/media/variants/(thumb|small|medium|large)/media-[0-9]+\.jpg\z", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                $"Storage key '{storageKey}' is outside the public prefixes and has no public URL.");
        }

        return objectStore.GetPublicUrl(storageKey);
    }
}
