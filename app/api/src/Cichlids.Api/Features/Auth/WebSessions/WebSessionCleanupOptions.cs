namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>
/// Settings of the cleanup of web sessions that cannot be used any more, bound from the
/// "WebSessionCleanup" configuration section.
/// </summary>
public sealed class WebSessionCleanupOptions
{
    public const string SectionName = "WebSessionCleanup";

    /// <summary>
    /// Whether the hosted cleanup loop runs at all. Disabled in the test host of the web session
    /// tests, which run cleanup passes directly.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Time between two cleanup passes.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Time without use after which a web session is deleted. The default equals the lifetime of
    /// the session cookie, which the web app renews each time it opens, so the browser of a
    /// session unused for that long no longer holds the cookie.
    /// </summary>
    public TimeSpan Retention { get; set; } = WebSessionCookies.SessionLifetime;

    /// <summary>
    /// Maximum number of sessions one cleanup pass deletes. A full batch is followed by the next
    /// pass at once.
    /// </summary>
    public int BatchSize { get; set; } = 100;
}
