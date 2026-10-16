using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Issues and reads the short-lived URLs under which the variants of care photos are served. A care
/// photo lives under a private storage prefix, so its bytes reach a client only through these URLs.
/// A URL holds a protected, time-limited token for one variant; it stops working when the lifetime
/// has passed, when it was altered or when it was not issued by this application.
/// </summary>
public sealed class CarePhotoUrlSigner(IDataProtectionProvider provider)
{
    /// <summary>
    /// The data protection purpose of the tokens.
    /// </summary>
    public const string Purpose = "Cichlids.CarePhotoUrl";

    /// <summary>
    /// How long an issued URL keeps working.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    /// <summary>
    /// The path of the URL that serves the variant with the given id.
    /// </summary>
    public string PathFor(long variantId) =>
        $"/api/care-photos/{_protector.Protect(variantId.ToString(CultureInfo.InvariantCulture), Lifetime)}";

    /// <summary>
    /// The id of the variant a token names, or null when the token is not valid.
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
