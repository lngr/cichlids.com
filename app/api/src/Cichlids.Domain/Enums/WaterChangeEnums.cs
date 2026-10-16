namespace Cichlids.Domain.Enums;

/// <summary>
/// The steps of a guided water change that a member checks off or skips. The two photo steps
/// record only that the member skipped taking the photo; a taken photo is stored with the session.
/// </summary>
public enum WaterChangeStep
{
    HeaterFilterOff,
    DrainingStarted,
    DrainedPhoto,
    Conditioner,
    TemperatureMatched,
    RefillStarted,
    RefilledPhoto,
    HeaterFilterOn,
}

public enum WaterChangeStepState { Done, Skipped }

/// <summary>
/// The moment of a guided water change a photo documents.
/// </summary>
public enum WaterChangePhotoSlot { Drained, Refilled }
