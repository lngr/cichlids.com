using Cichlids.Api.Features.Profiles;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// The authenticated caller's own profile plus the realm roles their token grants, so a client
/// can decide which moderation affordances to show without decoding the token itself.
/// </summary>
public sealed record MeDto(ProfileDetailDto Profile, IReadOnlyList<string> Roles);
