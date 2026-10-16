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
