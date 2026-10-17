using Microsoft.Extensions.Caching.Memory;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Remembers per process which member subjects had their activity recorded, so a subject
/// causes at most one statement per process and recording interval. The memory is bounded: it
/// holds a limited number of subjects, each entry expires after the interval, and a full cache
/// admits no new subject until entries expire (those subjects then reach the database on every
/// request, which the statement itself handles).
/// </summary>
public sealed class MemberActivityThrottle : IDisposable
{
    public const int MaxSubjects = 50_000;

    private readonly MemoryCache _recorded = new(new MemoryCacheOptions { SizeLimit = MaxSubjects });

    /// <summary>
    /// Whether the subject was recorded within the recording interval.
    /// </summary>
    public bool IsRecent(string subject) => _recorded.TryGetValue(subject, out _);

    /// <summary>
    /// Remembers the subject for the recording interval.
    /// </summary>
    public void Remember(string subject) =>
        _recorded.Set(subject, true, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = MemberActivityService.RecordInterval,
        });

    public void Dispose() => _recorded.Dispose();
}
