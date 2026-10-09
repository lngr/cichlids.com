namespace Cichlids.Domain.Enums;

/// <summary>
/// Whether a discussion thread is read-only archive content or open to new posts through the
/// platform's own identity (ADR-0021).
/// </summary>
public enum DiscussionThreadState
{
    Archived,
    Open,
}
