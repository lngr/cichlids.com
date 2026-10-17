using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// Public member profile. Also covers profiles with no login (kind Archived or System),
/// created only to attribute migrated content to a stable owner.
/// </summary>
public class Profile
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }
    public string? ExternalAvatarUrl { get; set; }
    public ProfileKind Kind { get; set; } = ProfileKind.Member;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? SuspendedUntil { get; set; }

    /// <summary>
    /// When the member last changed the public handle; null while the handle has never been changed.
    /// </summary>
    public DateTimeOffset? HandleChangedAt { get; set; }

    /// <summary>
    /// When an authenticated request of the registered member was last recorded; null while the API has not seen the member. Private to the member.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>
    /// Start of an absence of 28 days or more that ended with a request of the member: the last_seen_at before that request. Null while no absence is recorded. Private to the member.
    /// </summary>
    public DateTimeOffset? AwaySince { get; set; }

    public DateTimeOffset? OnboardingCompletedAt { get; set; }
    public string[] Interests { get; set; } = [];
    public string[] Badges { get; set; } = [];

    /// <summary>
    /// Whether the water change and time-lapse streaks of the member are shown to the member and to other viewers. The streaks are evaluated either way.
    /// </summary>
    public bool StreaksEnabled { get; set; } = true;

    public List<ProfileIdentity> Identities { get; set; } = [];
}
