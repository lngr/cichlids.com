namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Raised when a provisional caller (device token) reaches an action that needs a registered
/// member.
/// </summary>
public sealed class RegistrationRequiredException()
    : Exception("The action needs a registered member; the caller is provisional.");
