using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// The checksum of a photo a member used as proof, kept per member and kind. The row stays when the
/// links of the photo go away or the file leaves the media library, so a photo counts as used before
/// for as long as the member exists.
/// </summary>
public class UsedChecksum
{
    public long ProfileId { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
    public UsedChecksumKind Kind { get; set; }
    public DateTimeOffset FirstUsedAt { get; set; }
}
