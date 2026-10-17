namespace Cichlids.Api.Features.Me;

/// <summary>
/// Tells whether the archive retrospective of a member is due. A due archive retrospective takes
/// precedence over the return retrospective.
/// </summary>
public interface IArchiveRetrospectiveGate
{
    Task<bool> IsDueAsync(long profileId, CancellationToken cancellationToken);
}

/// <summary>
/// The archive retrospective gate of a deployment without archive retrospectives: none is ever due.
/// </summary>
public sealed class NoArchiveRetrospectiveGate : IArchiveRetrospectiveGate
{
    public Task<bool> IsDueAsync(long profileId, CancellationToken cancellationToken) => Task.FromResult(false);
}
