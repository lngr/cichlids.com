using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Raised when the authenticated account is bound to a profile that is not a member profile, so
/// the login cannot act in the domain. It carries the id and kind of the bound profile.
/// </summary>
public sealed class NonMemberLoginException(long profileId, ProfileKind kind)
    : Exception($"The login is bound to profile {profileId} of kind {kind}, which is not a member profile.")
{
    public long ProfileId { get; } = profileId;

    public ProfileKind Kind { get; } = kind;
}
