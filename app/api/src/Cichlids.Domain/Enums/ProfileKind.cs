namespace Cichlids.Domain.Enums;

/// <summary>
/// Distinguishes a profile people can log into from profiles that only exist to attribute
/// migrated content whose original owner has no active account.
/// </summary>
public enum ProfileKind
{
    Member,
    Archived,
    System,
}
