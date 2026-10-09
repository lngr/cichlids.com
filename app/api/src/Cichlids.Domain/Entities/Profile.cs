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
    public long? ProfileImageMediaId { get; set; }
    public long? AvatarMediaId { get; set; }

    public List<ProfileIdentity> Identities { get; set; } = [];
}
