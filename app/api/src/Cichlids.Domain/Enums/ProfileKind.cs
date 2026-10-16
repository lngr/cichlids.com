namespace Cichlids.Domain.Enums;

/// <summary>
/// Distinguishes a profile people can log into from profiles that only exist to attribute
/// migrated content whose original owner has no active account, and from the provisional profile
/// of an app installation that has registered no account. A provisional profile authenticates
/// with a device credential, has a generated name and no public handle.
/// </summary>
public enum ProfileKind
{
    Member,
    Archived,
    System,
    Provisional,
}
