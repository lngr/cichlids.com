namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// The bytes of an uploaded photo together with the image format they were declared and verified to
/// have.
/// </summary>
public sealed record UploadedImage(byte[] Bytes, UploadImageFormat Format);

/// <summary>
/// Reads the file part of a photo upload and checks it against the upload limits: a non-empty file
/// of at most UploadsEndpoints.MaxFileBytes, a declared content type of JPEG, PNG or WebP and
/// content that carries the signature of that format.
/// </summary>
public static class UploadedImageReader
{
    /// <summary>
    /// Returns the image, or the message that explains why the file is refused.
    /// </summary>
    public static async Task<(UploadedImage? Image, string? Error)> ReadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return (null, "A non-empty file field is required.");
        }

        if (file.Length > UploadsEndpoints.MaxFileBytes)
        {
            return (null, $"The file exceeds the limit of {UploadsEndpoints.MaxFileBytes / (1024 * 1024)} MB.");
        }

        var format = UploadImageFormat.FromContentType(file.ContentType);
        if (format is null)
        {
            return (null, "The file must be image/jpeg, image/png or image/webp.");
        }

        var bytes = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }

        return format.MatchesSignature(bytes)
            ? (new UploadedImage(bytes, format), null)
            : (null, $"The file content is not {format.ContentType}.");
    }
}
