using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Cichlids.Api.Features.Media;

/// <summary>
/// Issues and reads the short-lived paths under which the private photos and videos of every context
/// are served: the photo variants and the HLS files of the videos that lie in the private storage area
/// reach a client only through these paths. A path holds a protected, time-limited token for one photo
/// variant or one video. A token stops working when its lifetime has passed, when it was altered, when it
/// was issued for the other kind of media or when it was not issued by this application. The token is the
/// credential: callers issue it only to a viewer who sees the media item.
/// </summary>
public sealed class MediaUrlSigner(IDataProtectionProvider provider)
{
    /// <summary>
    /// The route prefix of the photo variants, ending in a slash.
    /// </summary>
    public const string PhotoRoute = "/api/media/photos/";

    /// <summary>
    /// The route prefix of the videos, ending in a slash.
    /// </summary>
    public const string VideoRoute = "/api/media/videos/";

    /// <summary>
    /// How long an issued photo path keeps working.
    /// </summary>
    public static readonly TimeSpan PhotoLifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long an issued video path keeps working.
    /// </summary>
    public static readonly TimeSpan VideoLifetime = TimeSpan.FromHours(2);

    /// <summary>
    /// The data protection purpose of the photo tokens.
    /// </summary>
    public const string PhotoPurpose = "Cichlids.MediaPhotoUrl";

    /// <summary>
    /// The data protection purpose of the video tokens.
    /// </summary>
    public const string VideoPurpose = "Cichlids.MediaVideoUrl";

    private readonly ITimeLimitedDataProtector _photos = provider.CreateProtector(PhotoPurpose).ToTimeLimitedDataProtector();
    private readonly ITimeLimitedDataProtector _videos = provider.CreateProtector(VideoPurpose).ToTimeLimitedDataProtector();

    /// <summary>
    /// The path that serves the photo variant with the given id. The token carries the visibility
    /// generation of the media item, so it stops working when that value changes.
    /// </summary>
    public string PhotoPathFor(long variantId, long generation) =>
        PhotoRoute + _photos.Protect(Payload(variantId, generation), PhotoLifetime);

    /// <summary>
    /// The path of one file of the video with the given media item id, such as master.m3u8. The
    /// playlists name their segments with relative paths, so every request of a player carries the token.
    /// </summary>
    public string VideoPathFor(long mediaItemId, long generation, string file) =>
        $"{VideoRoute}{_videos.Protect(Payload(mediaItemId, generation), VideoLifetime)}/{file}";

    /// <summary>
    /// The variant id and the visibility generation a photo token names, or null when the token is not valid.
    /// </summary>
    public (long Id, long Generation)? TryReadPhoto(string token) => TryRead(_photos, token);

    /// <summary>
    /// The media item id and the visibility generation a video token names, or null when the token is not valid.
    /// </summary>
    public (long Id, long Generation)? TryReadVideo(string token) => TryRead(_videos, token);

    private static string Payload(long id, long generation) =>
        string.Create(CultureInfo.InvariantCulture, $"{id}:{generation}");

    private static (long, long)? TryRead(ITimeLimitedDataProtector protector, string token)
    {
        try
        {
            var parts = protector.Unprotect(token).Split(':');
            return parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var generation)
                ? (id, generation) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
