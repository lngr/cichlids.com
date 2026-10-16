using Cichlids.Api.Features.Me;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// How the content of a provisional profile reaches the Keycloak account of the caller: Upgrade when
/// the account has no profile yet and the provisional profile becomes it, Move when the account has a
/// member profile that receives the content.
/// </summary>
public enum ProvisionalRegistrationMode
{
    Upgrade,
    Move,
}

/// <summary>
/// The amounts of content a provisional profile holds: its tanks that are not deleted, the care
/// entries, media items and to-dos it owns and its vacations.
/// </summary>
public sealed record ProvisionalContentCounts(int Tanks, int CareEntries, int MediaItems, int Todos, int Vacations);

/// <summary>
/// What registering with a device proof does to the content: the mode, the amounts that would arrive
/// at the account, the vacations that overlap vacations of the account and are dropped, the
/// vacations dropped because they would repair a period of a tank of the account, and the display name of the member profile that receives the content (null for Upgrade).
/// </summary>
public sealed record ProvisionalPreviewDto(
    ProvisionalRegistrationMode Mode,
    ProvisionalContentCounts Content,
    int VacationsDropped,
    int VacationsDroppedRepair,
    string? TargetDisplayName);

/// <summary>
/// The result of a move: the amounts that arrived in the member profile, the number of vacations
/// dropped because they overlap vacations of the member, the number dropped because they would
/// repair a period of a tank of the member, and the member profile.
/// </summary>
public sealed record ProvisionalMoveDto(ProvisionalContentCounts Moved, int VacationsDropped, int VacationsDroppedRepair, MeDto Me);
