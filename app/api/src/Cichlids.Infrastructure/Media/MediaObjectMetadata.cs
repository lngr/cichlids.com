using System.Globalization;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Infrastructure.Media;

/// <summary>
/// Whether a stored object matches what the media pipeline would produce from its source.
/// </summary>
public enum ObjectState
{
    /// <summary>The object exists and was produced from the same source by the same pipeline version.</summary>
    Current,

    /// <summary>No object exists under the key.</summary>
    Missing,

    /// <summary>An object exists but its source checksum or pipeline version differs, or it carries no metadata.</summary>
    Differs,
}

/// <summary>
/// Pixel size of a photo original and whether it decodes. An undecodable photo has the size its
/// header declares when it is in a format browsers render, otherwise no size.
/// </summary>
public sealed record PhotoInfo(int? Width, int? Height, bool Decodable)
{
    public PhotoInfo(int width, int height)
        : this(width, height, true)
    {
    }

    public static PhotoInfo Undecodable { get; } = new(null, null, false);
}

/// <summary>
/// The user metadata every stored original and variant carries, and the rule that compares it
/// with the source an object would be produced from. The checksum of the stored bytes (sha256),
/// the checksum of the source file (source-sha256) and the pipeline version identify an object's
/// content without reading it; photo originals also record their width, height and whether the
/// image decodes, and the public copy of an undecodable original records its width and height.
/// </summary>
public static class MediaObjectMetadata
{
    public const string Sha256Key = "sha256";
    public const string SourceSha256Key = "source-sha256";
    public const string PipelineKey = "pipeline";
    public const string WidthKey = "width";
    public const string HeightKey = "height";
    public const string DecodableKey = "decodable";

    /// <summary>
    /// Version of the derivation that turns a source file into a stored original: the source with
    /// its personal metadata removed by MediaMetadataSanitizer.
    /// </summary>
    public const int OriginalPipelineVersion = 2;

    /// <summary>
    /// Version of the derivation that turns an original into a stored variant: one decode, resized
    /// to the variant width, converted to sRGB and encoded as JPEG without metadata.
    /// </summary>
    public const int VariantPipelineVersion = 2;

    /// <summary>
    /// Builds the metadata of an original. Photo information is added for photo originals only.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ForOriginal(string sha256, string sourceSha256, PhotoInfo? photo)
    {
        var metadata = new Dictionary<string, string>
        {
            [Sha256Key] = sha256,
            [SourceSha256Key] = sourceSha256,
            [PipelineKey] = OriginalPipelineVersion.ToString(CultureInfo.InvariantCulture),
        };

        if (photo is not null)
        {
            metadata[DecodableKey] = photo.Decodable ? "true" : "false";
            if (photo.Width is { } width)
            {
                metadata[WidthKey] = width.ToString(CultureInfo.InvariantCulture);
            }

            if (photo.Height is { } height)
            {
                metadata[HeightKey] = height.ToString(CultureInfo.InvariantCulture);
            }
        }

        return metadata;
    }

    /// <summary>
    /// Builds the metadata of a variant generated from a source file.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ForVariant(string sha256, string sourceSha256) =>
        new Dictionary<string, string>
        {
            [Sha256Key] = sha256,
            [SourceSha256Key] = sourceSha256,
            [PipelineKey] = VariantPipelineVersion.ToString(CultureInfo.InvariantCulture),
        };

    /// <summary>
    /// Reads the photo information recorded on an original, or null when the object carries none.
    /// </summary>
    public static PhotoInfo? ReadPhotoInfo(ObjectHead head)
    {
        if (!head.Metadata.TryGetValue(DecodableKey, out var decodable))
        {
            return null;
        }

        var size = ReadSize(head);
        if (decodable != "true")
        {
            return size is { } declared ? new PhotoInfo(declared.Width, declared.Height, false) : PhotoInfo.Undecodable;
        }

        return size is { } measured ? new PhotoInfo(measured.Width, measured.Height) : null;
    }

    /// <summary>
    /// Compares a stored object with the source it would be produced from. A null head means no
    /// object exists. An object is current when its source checksum equals the given one and its
    /// pipeline equals the expected version; any other object, including one without metadata,
    /// differs.
    /// </summary>
    public static ObjectState Decide(ObjectHead? head, string sourceSha256, int expectedPipelineVersion)
    {
        if (head is null)
        {
            return ObjectState.Missing;
        }

        var matches =
            head.Metadata.TryGetValue(SourceSha256Key, out var storedSource)
            && string.Equals(storedSource, sourceSha256, StringComparison.Ordinal)
            && head.Metadata.TryGetValue(PipelineKey, out var storedPipeline)
            && string.Equals(storedPipeline, expectedPipelineVersion.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        return matches ? ObjectState.Current : ObjectState.Differs;
    }

    /// <summary>
    /// Builds the metadata of the public copy of an undecodable original: the variant metadata
    /// and the width and height its header declares, so the copy is recorded without reading it.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ForPublicCopy(string sha256, string sourceSha256, int width, int height) =>
        new Dictionary<string, string>(ForVariant(sha256, sourceSha256))
        {
            [WidthKey] = width.ToString(CultureInfo.InvariantCulture),
            [HeightKey] = height.ToString(CultureInfo.InvariantCulture),
        };

    /// <summary>
    /// Reads the width and height recorded on an object, or null when it carries none.
    /// </summary>
    public static (int Width, int Height)? ReadSize(ObjectHead head) =>
        head.Metadata.TryGetValue(WidthKey, out var width)
        && head.Metadata.TryGetValue(HeightKey, out var height)
        && int.TryParse(width, NumberStyles.None, CultureInfo.InvariantCulture, out var w)
        && int.TryParse(height, NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            ? (w, h)
            : null;
}
