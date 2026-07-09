namespace Cichlids.Etl.Media;

/// <summary>
/// Storage key conventions the media pipeline derives from an original's own key: the variant
/// prefix a label lives under, and the content type a key's extension implies.
/// </summary>
public static class MediaStorageKeys
{
    private const string OriginalsPrefix = "originals/";
    private const string ForumAttachmentsPrefix = "forum_attachments/";

    /// <summary>
    /// Builds the storage key for one variant of an original: variants/, the label, then the
    /// original's own full storage key with .jpg appended. Keeping the original key's prefix and
    /// extension intact in the path, rather than stripping them, keeps variant keys unique across
    /// originals that would otherwise collide, such as two originals under different prefixes
    /// sharing a filename tail, or two originals in the same folder differing only in extension.
    /// </summary>
    public static string BuildVariantKey(string originalStorageKey, string label)
    {
        if (!originalStorageKey.StartsWith(OriginalsPrefix, StringComparison.Ordinal)
            && !originalStorageKey.StartsWith(ForumAttachmentsPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Storage key '{originalStorageKey}' is neither an original nor a forum attachment.", nameof(originalStorageKey));
        }

        return $"variants/{label}/{originalStorageKey}.jpg";
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
}
