using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// How the caller follows a tank: not at all, with a request the owner has not answered, or as an
/// approved follower.
/// </summary>
public enum TankFollowState
{
    None,
    Requested,
    Following,
}

/// <summary>
/// The follow relation of the caller to a tank. CanFollow tells whether the caller may send a follow
/// request or follow the tank (an account that does not own the tank and a profile level of public or
/// followers). Teaser is set only when the caller does not see the tank itself and the tank takes follow
/// requests: the owner to ask.
/// </summary>
public sealed record TankFollowDto(TankFollowState State, bool CanFollow, TankFollowTeaserDto? Teaser = null);

/// <summary>
/// The owner of a tank that takes follow requests, shown to members who do not see the tank: the public
/// identity of the owner and nothing of the tank.
/// </summary>
public sealed record TankFollowTeaserDto(AuthorDto Author);

/// <summary>
/// The audience of a tank as the owner sees it: the number of approved followers, the open follow
/// requests and the selected members.
/// </summary>
public sealed record TankAudienceDto(int Followers, int PendingRequests, int SelectedMembers);

/// <summary>
/// A follower, a pending requester or a selected member of a tank. Since is the time of the approval, the
/// request or the selection.
/// </summary>
public sealed record TankMemberDto(AuthorDto Member, DateTimeOffset Since);

/// <summary>
/// The member to select, named by the public handle (the username) of the profile.
/// </summary>
public sealed record AddSelectedMemberRequest(string? Handle);
