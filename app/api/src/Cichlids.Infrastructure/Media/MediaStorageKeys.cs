namespace Cichlids.Infrastructure.Media;

/// <summary>
/// Storage key conventions the media pipeline derives from an original's own key: the variant
/// prefix a label lives under, the key of an undecodable original's public copy, and the content
/// type a key's extension implies.
/// </summary>
public static class MediaStorageKeys
{
    private const string OriginalsPrefix = "originals/";
    private const string ForumAttachmentsPrefix = "forum_attachments/";
    private const string VariantsPrefix = "variants/";
    private const string PrivatePrefix = "private/";

    /// <summary>
    /// The key prefixes whose objects anyone may read without credentials: the derived variants
    /// and the forum attachments. Originals and everything under the private prefix live outside
    /// these prefixes and stay private.
    /// </summary>
    public static IReadOnlyList<string> PublicPrefixes { get; } = [VariantsPrefix, ForumAttachmentsPrefix];

    /// <summary>
    /// Builds the storage key for one variant of an original: variants/, the label, then the
    /// original's own full storage key with .jpg appended. Keeping the original key's prefix and
    /// extension intact in the path, rather than stripping them, keeps variant keys unique across
    /// originals that would otherwise collide, such as two originals under different prefixes
    /// sharing a filename tail, or two originals in the same folder differing only in extension.
    /// </summary>
    public static string BuildVariantKey(string originalStorageKey, string label)
    {
        EnsureOriginalKey(originalStorageKey);
        return $"{VariantsPrefix}{label}/{originalStorageKey}.jpg";
    }

    /// <summary>
    /// Whether a key is a public variant key: a variant or HLS file under the public variant prefix.
    /// </summary>
    public static bool IsPublicVariantKey(string key) => key.StartsWith(VariantsPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Whether a key is a private variant key: a variant or HLS file under the private variant prefix.
    /// </summary>
    public static bool IsPrivateVariantKey(string key) => key.StartsWith(PrivatePrefix + VariantsPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The private counterpart of a public variant key: the same path below the private prefix.
    /// </summary>
    public static string ToPrivateVariantKey(string publicVariantKey) => IsPublicVariantKey(publicVariantKey)
        ? PrivatePrefix + publicVariantKey
        : throw new ArgumentException($"Storage key '{publicVariantKey}' is not a public variant key.", nameof(publicVariantKey));

    /// <summary>
    /// The public counterpart of a private variant key: the same path without the private prefix.
    /// </summary>
    public static string ToPublicVariantKey(string privateVariantKey) => IsPrivateVariantKey(privateVariantKey)
        ? privateVariantKey[PrivatePrefix.Length..]
        : throw new ArgumentException($"Storage key '{privateVariantKey}' is not a private variant key.", nameof(privateVariantKey));

    /// <summary>
    /// Builds the storage key of a private original: private/, the area, the owner's profile id and
    /// the file name. Objects under the private prefix are never reachable without credentials.
    /// </summary>
    public static string BuildPrivateOriginalKey(string area, long ownerProfileId, string fileName) =>
        $"{PrivatePrefix}{area}/{ownerProfileId}/{fileName}";

    /// <summary>
    /// Builds the storage key of one variant of a private original: private/variants/, the label,
    /// then the original's own full storage key with .jpg appended. The key stays under the private
    /// prefix, so the variant is as private as its original.
    /// </summary>
    public static string BuildPrivateVariantKey(string privateOriginalStorageKey, string label)
    {
        if (!privateOriginalStorageKey.StartsWith(PrivatePrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Storage key '{privateOriginalStorageKey}' is not a private original.", nameof(privateOriginalStorageKey));
        }

        return $"{PrivatePrefix}variants/{label}/{privateOriginalStorageKey}.jpg";
    }

    /// <summary>
    /// Builds the storage key of the private original of a video: private/video/, the owner's
    /// profile id and the file name.
    /// </summary>
    public static string BuildPrivateVideoKey(long ownerProfileId, string fileName) =>
        $"{PrivatePrefix}video/{ownerProfileId}/{fileName}";

    /// <summary>
    /// Builds the storage directory, ending in a slash, that holds the HLS files of a video: the
    /// original's owner and file name without extension below variants/video/, or below
    /// private/variants/video/ for a private video.
    /// </summary>
    public static string BuildVideoDirectory(string privateVideoKey, bool isPrivate)
    {
        const string originalPrefix = PrivatePrefix + "video/";
        if (!privateVideoKey.StartsWith(originalPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Storage key '{privateVideoKey}' is not a video original.", nameof(privateVideoKey));
        }

        var relative = privateVideoKey[originalPrefix.Length..];
        var dot = relative.LastIndexOf('.');
        var withoutExtension = dot < 0 ? relative : relative[..dot];
        return isPrivate
            ? $"{PrivatePrefix}{VariantsPrefix}video/{withoutExtension}/"
            : $"{VariantsPrefix}video/{withoutExtension}/";
    }

    private static readonly System.Text.RegularExpressions.Regex HlsFile = new(
        @"\A(master\.m3u8|poster\.jpg|[0-9]{2,4}p/(index\.m3u8|init(_[0-9]+)?\.mp4|seg_[0-9]{5}\.m4s))\z",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether a path relative to a video's HLS directory names one of the files a transcode
    /// writes: the master playlist, the poster, or the playlist, init segment or media segment of
    /// a rendition.
    /// </summary>
    public static bool IsHlsFile(string path) => HlsFile.IsMatch(path);

    /// <summary>
    /// The content type of a file of an HLS directory, by its extension.
    /// </summary>
    public static string ResolveHlsContentType(string path) => Path.GetExtension(path) switch
    {
        ".m3u8" => "application/vnd.apple.mpegurl",
        ".jpg" => "image/jpeg",
        _ => "video/mp4",
    };

    /// <summary>
    /// Guesses the content type for an original from its storage key's extension, falling back to
    /// a generic binary type for anything unrecognized (for example a video format the pipeline
    /// otherwise never inspects).
    /// </summary>
    public static string ResolveContentType(string storageKey)
    {
        var lastDot = storageKey.LastIndexOf('.');
        var extension = lastDot < 0 ? string.Empty : storageKey[(lastDot + 1)..].ToLowerInvariant();

        return extension switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "webp" => "image/webp",
            "tif" or "tiff" => "image/tiff",
            "mp4" or "m4v" => "video/mp4",
            "mpg" or "mpeg" or "mp2" or "m2v" => "video/mpeg",
            "avi" => "video/x-msvideo",
            "wmv" => "video/x-ms-wmv",
            "flv" => "video/x-flv",
            "webm" => "video/webm",
            "mkv" => "video/x-matroska",
            "3gp" or "3g2" => "video/3gpp",
            _ => "application/octet-stream",
        };
    }

    /// <summary>
    /// Builds the storage key of the public copy of an original that cannot be decoded:
    /// variants/copy/ followed by the original's own full storage key. The copy keeps the
    /// original's format, so its key keeps the original's extension.
    /// </summary>
    public static string BuildPublicCopyKey(string originalStorageKey)
    {
        EnsureOriginalKey(originalStorageKey);
        return $"{VariantsPrefix}{MediaVariantSpec.PublicCopyLabel}/{originalStorageKey}";
    }

    private static void EnsureOriginalKey(string originalStorageKey)
    {
        if (!originalStorageKey.StartsWith(OriginalsPrefix, StringComparison.Ordinal)
            && !originalStorageKey.StartsWith(ForumAttachmentsPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Storage key '{originalStorageKey}' is neither an original nor a forum attachment.", nameof(originalStorageKey));
        }
    }
}
