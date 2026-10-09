namespace Cichlids.Infrastructure.Identity;

/// <summary>
/// The rule a value has to meet to become a member's public handle without change: it is not
/// blank and contains no email address, because a handle is shown to everyone.
/// </summary>
public static class PublicHandle
{
    /// <summary>
    /// Returns true when the value can serve as a public handle as it is.
    /// </summary>
    public static bool IsUsable(string? value) => !string.IsNullOrWhiteSpace(value) && !EmailLike.Contains(value);
}
