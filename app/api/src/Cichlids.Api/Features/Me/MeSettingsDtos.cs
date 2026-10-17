namespace Cichlids.Api.Features.Me;

/// <summary>
/// The settings of the authenticated member. StreaksEnabled tells whether the water change and
/// time-lapse streaks are shown to the member and to other viewers.
/// </summary>
public sealed record MeSettingsDto(bool StreaksEnabled);

/// <summary>
/// Changes the settings of the authenticated member; every value must be present.
/// </summary>
public sealed record UpdateMeSettingsRequest(bool? StreaksEnabled);

/// <summary>
/// The public handle the member asks for. The API lowercases and checks it.
/// </summary>
public sealed record ChangeHandleRequest(string? Handle);

/// <summary>
/// The public handle of the member after a change request and the earliest time of the next change.
/// </summary>
public sealed record HandleChangeDto(string Handle, DateTimeOffset? NextChangeAt);
