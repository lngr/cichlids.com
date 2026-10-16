namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// Limits of the device authentication endpoints, bound from the "DeviceAuth" configuration
/// section. The request limits count per network address in the memory of one API instance.
/// </summary>
public sealed class DeviceAuthOptions
{
    public const string SectionName = "DeviceAuth";

    /// <summary>How long a challenge can be signed after the API issued it.</summary>
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The largest difference between the timestamp of a signed challenge and the time of the API.</summary>
    public TimeSpan TimestampWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Key registrations one network address may make within an hour.</summary>
    public int MaxRegistrationsPerAddressPerHour { get; set; } = 30;

    /// <summary>Challenge and token requests one network address may make within a minute.</summary>
    public int MaxTokenRequestsPerAddressPerMinute { get; set; } = 120;
}
