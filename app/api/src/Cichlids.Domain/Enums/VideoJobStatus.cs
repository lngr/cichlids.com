namespace Cichlids.Domain.Enums;

/// <summary>
/// Where a video transcoding job stands: waiting in the queue, claimed by a worker, finished or
/// given up after its last attempt.
/// </summary>
public enum VideoJobStatus
{
    Queued,
    Running,
    Done,
    Failed,
}
