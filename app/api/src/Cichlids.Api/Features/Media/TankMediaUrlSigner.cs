using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Cichlids.Api.Features.Media;

/// <summary>
/// Issues and reads the short-lived paths under which the private media of a tank are served: the
/// photo variants and the HLS files of the videos of a tank whose profile is not public live under
/// the private storage prefix, so their bytes reach a client only through these paths. A path holds
/// a protected, time-limited token for one photo variant or one video. A token stops working when its
/// lifetime has passed, when it was altered, when it was issued for the other kind of media or when
/// it was not issued by this application. The token is the credential: callers issue it only to a
/// viewer who sees the tank.
/// </summary>
public sealed class TankMediaUrlSigner(IDataProtectionProvider provider)
{
    /// <summary>
    /// The route prefix of the photo variants, ending in a slash.
    /// </summary>
    public const string PhotoRoute = "/api/tank-media/";

    /// <summary>
    /// The route prefix of the videos, ending in a slash.
    /// </summary>
    public const string VideoRoute = "/api/tank-videos/";

    /// <summary>
    /// How long an issued photo path keeps working.
    /// </summary>
    public static readonly TimeSpan PhotoLifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long an issued video path keeps working.
    /// </summary>
    public static readonly TimeSpan VideoLifetime = TimeSpan.FromHours(2);

    private readonly ITimeLimitedDataProtector _photos = provider.CreateProtector("Cichlids.TankPhotoUrl").ToTimeLimitedDataProtector();
    private readonly ITimeLimitedDataProtector _videos = provider.CreateProtector("Cichlids.TankVideoUrl").ToTimeLimitedDataProtector();

    /// <summary>
    /// The path that serves the photo variant with the given id.
    /// </summary>
    public string PhotoPathFor(long variantId) =>
        PhotoRoute + _photos.Protect(variantId.ToString(CultureInfo.InvariantCulture), PhotoLifetime);

    /// <summary>
    /// The path of one file of the video with the given media item id, such as master.m3u8. The
    /// playlists name their segments with relative paths, so every request of a player carries the token.
    /// </summary>
    public string VideoPathFor(long mediaItemId, string file) =>
        $"{VideoRoute}{_videos.Protect(mediaItemId.ToString(CultureInfo.InvariantCulture), VideoLifetime)}/{file}";

    /// <summary>
    /// The variant id a photo token names, or null when the token is not valid.
    /// </summary>
    public long? TryReadPhoto(string token) => TryRead(_photos, token);

    /// <summary>
    /// The media item id a video token names, or null when the token is not valid.
    /// </summary>
    public long? TryReadVideo(string token) => TryRead(_videos, token);

    private static long? TryRead(ITimeLimitedDataProtector protector, string token)
    {
        try
        {
            return long.TryParse(protector.Unprotect(token), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
