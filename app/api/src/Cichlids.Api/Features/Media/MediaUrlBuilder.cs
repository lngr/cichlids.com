using System.Text.RegularExpressions;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Features.Media;

/// <summary>
/// Builds media URLs from a media item's variant storage keys, resolving every key through the
/// configured object store so callers never see storage keys directly. A key under the private
/// prefix has no public URL and resolves to nothing; any other key outside the public prefixes, such
/// as an original's, throws, so a private object never reaches a response.
/// </summary>
public sealed class MediaUrlBuilder(IObjectStore objectStore, MediaUrlSigner? privateSigner = null)
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
    /// The URLs of the sized variants of a media item whose variants may live under the private prefix
    /// while its placement follows a change of visibility. The visibility rule decides the kind of URL:
    /// an item that anonymous viewers see (anonymousVisible) gets the public
    /// URL of a variant that lies under a public prefix, and every other combination resolves to a signed
    /// path when signed paths are allowed and the builder has a signer, and to null otherwise. A variant
    /// of an item anonymous viewers do not see never gets a public URL, even while its object lies
    /// under the public prefix. Callers allow signed paths only for a viewer who sees the media item, and
    /// never for a response that is cached across viewers. A signed path carries the visibility generation that
    /// was read together with the visibility check of the viewer (issuableGeneration); without one no signed path
    /// is issued.
    /// </summary>
    public ImageUrlsDto Build(IEnumerable<MediaVariant> variants, bool allowSigned, bool anonymousVisible, long? issuableGeneration)
    {
        var byLabel = variants.ToDictionary(v => v.Label);
        string? ByLabel(string label) => byLabel.TryGetValue(label, out var variant) ? UrlOf(variant, allowSigned, anonymousVisible, issuableGeneration) : null;
        if (!LabelsLargestFirst.Any(byLabel.ContainsKey) && ByLabel(MediaVariantSpec.PublicCopyLabel) is { } copy)
        {
            return new ImageUrlsDto(copy, copy, copy, copy);
        }

        return new ImageUrlsDto(ByLabel("thumb"), ByLabel("small"), ByLabel("medium"), ByLabel("large"));
    }

    private string? UrlOf(MediaVariant variant, bool allowSigned, bool anonymousVisible, long? issuableGeneration) =>
        anonymousVisible && !variant.StorageKey.StartsWith(PrivatePrefix, StringComparison.Ordinal)
            ? PublicUrl(variant.StorageKey)
            : allowSigned && privateSigner is not null && issuableGeneration is { } generation ? privateSigner.PhotoPathFor(variant.Id, generation) : null;

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
    /// The URL of the thumbnail, otherwise of the public copy, or null when neither key is given or the
    /// key lies under the private prefix.
    /// </summary>
    public string? BuildThumbnail(string? thumbStorageKey, string? copyStorageKey) =>
        thumbStorageKey is not null ? PublicUrl(thumbStorageKey)
        : copyStorageKey is not null ? PublicUrl(copyStorageKey)
        : null;

    private string? FindUrl(IReadOnlyDictionary<string, string> variantStorageKeysByLabel, string label) =>
        variantStorageKeysByLabel.TryGetValue(label, out var storageKey)
            ? PublicUrl(storageKey)
            : null;

    private string? PublicUrl(string storageKey)
    {
        if (storageKey.StartsWith(PrivatePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (!MediaStorageKeys.PublicPrefixes.Any(prefix => storageKey.StartsWith(prefix, StringComparison.Ordinal))
            && !Regex.IsMatch(storageKey, @"\Agolden/[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}/media/variants/(thumb|small|medium|large)/media-[0-9]+\.jpg\z", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                $"Storage key '{storageKey}' is outside the public prefixes and has no public URL.");
        }

        return objectStore.GetPublicUrl(storageKey);
    }
}
