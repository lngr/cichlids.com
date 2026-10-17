namespace Cichlids.Domain;

/// <summary>
/// Facts about profiles that come from the legacy archive.
/// </summary>
public static class LegacyProfiles
{
    /// <summary>
    /// The creation time of a legacy profile whose archive rows carry no creation time. The value lies
    /// before the launch of the site and marks the creation time as unknown.
    /// </summary>
    public static readonly DateTimeOffset UnknownCreatedAt = new(2003, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Whether the creation time of a profile is a real moment and not the marker for an unknown one.
    /// </summary>
    public static bool HasKnownCreatedAt(DateTimeOffset createdAt) => createdAt != UnknownCreatedAt;
}
