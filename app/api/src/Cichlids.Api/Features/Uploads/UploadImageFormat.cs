namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// An image format the upload endpoint accepts, with the content type a client declares for it
/// and the extension its stored original gets.
/// </summary>
public sealed record UploadImageFormat(string ContentType, string Extension)
{
    public static readonly UploadImageFormat Jpeg = new("image/jpeg", "jpg");
    public static readonly UploadImageFormat Png = new("image/png", "png");
    public static readonly UploadImageFormat Webp = new("image/webp", "webp");

    private static readonly UploadImageFormat[] Accepted = [Jpeg, Png, Webp];

    /// <summary>
    /// The accepted format for a declared content type, or null for any other content type.
    /// Parameters such as a charset are ignored.
    /// </summary>
    public static UploadImageFormat? FromContentType(string? contentType)
    {
        var mediaType = contentType?.Split(';')[0].Trim();
        return Accepted.FirstOrDefault(f => string.Equals(f.ContentType, mediaType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether the leading bytes carry this format's file signature: FF D8 FF for JPEG, the
    /// eight-byte PNG signature, and a RIFF container of type WEBP.
    /// </summary>
    public bool MatchesSignature(ReadOnlySpan<byte> bytes)
    {
        if (this == Jpeg)
        {
            return bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]);
        }

        if (this == Png)
        {
            return bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        }

        return bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8);
    }
}
