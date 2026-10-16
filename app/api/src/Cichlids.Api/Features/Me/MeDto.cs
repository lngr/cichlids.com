using Cichlids.Api.Features.Profiles;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// The authenticated caller's own profile plus the realm roles their token grants, so a client
/// can decide which moderation affordances to show without decoding the token itself. Provisional
/// is true for the caller of a device token, whose profile has a generated name and no public handle.
/// </summary>
public sealed record MeDto(ProfileDetailDto Profile, IReadOnlyList<string> Roles, bool Provisional = false);
