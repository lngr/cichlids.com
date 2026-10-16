namespace Cichlids.Domain.Entities;

/// <summary>
/// One answer of a tank owner to the question whether the tank should be public: the answer, the
/// time it was given and where. The newest row of a tank is the current consent. A tank that
/// existed before the question was asked has no row.
/// </summary>
public class TankVisibilityConsent
{
    public const string CreationContext = "creation";
    public const string SettingsContext = "settings";

    public long Id { get; set; }
    public long TankId { get; set; }

    /// <summary>
    /// True when the owner agreed that others can see and follow the tank.
    /// </summary>
    public bool Answer { get; set; }

    public DateTimeOffset AnsweredAt { get; set; }

    /// <summary>
    /// Where the answer was given: creation or settings.
    /// </summary>
    public string Context { get; set; } = CreationContext;
}
