using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Cichlids.Api.Features.Videos;

/// <summary>
/// Issues and reads the paths under which the files of a care video are served. The HLS files of
/// a care video live under a private storage prefix, so a player reaches them only through a path
/// that starts with a protected, time-limited token for the video. The playlists name their
/// segments with relative paths, which a player resolves against the path of the playlist, so every
/// request carries the token and no playlist is rewritten. A token stops working when its lifetime
/// has passed, when it was altered or when it was not issued by this application.
/// </summary>
public sealed class CareVideoUrlSigner(IDataProtectionProvider provider)
{
    /// <summary>
    /// The data protection purpose of the tokens.
    /// </summary>
    public const string Purpose = "Cichlids.CareVideoUrl";

    /// <summary>
    /// How long an issued path keeps working.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    /// <summary>
    /// The path of one file of the video with the given media item id, such as master.m3u8.
    /// </summary>
    public string PathFor(long mediaItemId, string file) =>
        $"/api/care-videos/{_protector.Protect(mediaItemId.ToString(CultureInfo.InvariantCulture), Lifetime)}/{file}";

    /// <summary>
    /// The media item id a token names, or null when the token is not valid.
    /// </summary>
    public long? TryRead(string token)
    {
        try
        {
            return long.TryParse(_protector.Unprotect(token), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
