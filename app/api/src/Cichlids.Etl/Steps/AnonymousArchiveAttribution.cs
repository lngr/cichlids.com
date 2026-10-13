using Cichlids.Etl.Runtime;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Attributes the anonymous legacy records of one kind (fe_user 0) to the community archive
/// profile during a single step run. The profile is looked up once and reused for every record;
/// the records are counted and summarized in one report line per kind.
/// </summary>
public sealed class AnonymousArchiveAttribution
{
    private readonly EtlContext _context;
    private readonly StepStatistics _stats;
    private readonly string _kind;
    private long? _profileId;
    private int _count;

    public AnonymousArchiveAttribution(EtlContext context, StepStatistics stats, string kind)
    {
        _context = context;
        _stats = stats;
        _kind = kind;
    }

    /// <summary>
    /// Counts one anonymous record and returns the id of the community archive profile.
    /// </summary>
    public async Task<long> ResolveAsync(CancellationToken cancellationToken)
    {
        _count++;
        _profileId ??= await PlaceholderProfiles.EnsureCommunityArchiveProfileAsync(_context, cancellationToken);
        return _profileId.Value;
    }

    /// <summary>
    /// Adds the summary line for the counted records to the step report. Adds nothing when no
    /// anonymous record occurred.
    /// </summary>
    public void Report()
    {
        if (_count > 0)
        {
            _stats.AddWarning($"anonymous {_kind} (fe_user 0): {_count} attributed to the community archive profile.");
        }
    }
}
