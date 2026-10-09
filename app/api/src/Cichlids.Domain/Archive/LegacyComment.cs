namespace Cichlids.Domain.Archive;

/// <summary>
/// A comment from the legacy platform that could not be migrated into the domain model
/// (for example: no resolvable target or author), kept in full source form for later reuse
/// instead of being discarded during the migration.
/// </summary>
public class LegacyComment
{
    public long Id { get; set; }
    public int LegacyId { get; set; }
    public LegacyTargetType LegacyTargetType { get; set; }
    public int LegacyTargetId { get; set; }
    public int? AuthorLegacyUserId { get; set; }
    public string? PosterName { get; set; }
    public string? Body { get; set; }
    public short? Stars { get; set; }
    public DateTimeOffset? CreatedAtLegacy { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string VaultReason { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; }
}
