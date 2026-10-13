using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>
/// Encrypts the Keycloak tokens of web sessions for storage with ASP.NET Core Data Protection under
/// a purpose of their own, and decrypts them again. A value that the key ring cannot decrypt, for
/// example one written with another key ring, yields no token.
/// </summary>
public sealed class WebSessionTokenProtector(IDataProtectionProvider provider)
{
    /// <summary>The Data Protection purpose of the stored web session tokens.</summary>
    public const string Purpose = "Cichlids.Api.WebSession.Tokens.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Encrypt(string token) => _protector.Protect(token);

    /// <summary>Decrypts a stored token; returns false when the key ring cannot decrypt it.</summary>
    public bool TryDecrypt(string encrypted, out string token)
    {
        try
        {
            token = _protector.Unprotect(encrypted);
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            token = string.Empty;
            return false;
        }
    }
}
