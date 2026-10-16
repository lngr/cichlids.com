namespace Cichlids.Api.Features.WebPush;

/// <summary>
/// Settings of the loop that sends Web Push reminders, bound from the "WebPushSender"
/// configuration section.
/// </summary>
public sealed class WebPushSenderOptions
{
    public const string SectionName = "WebPushSender";

    /// <summary>Whether the hosted loop runs at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Time between two passes.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}
