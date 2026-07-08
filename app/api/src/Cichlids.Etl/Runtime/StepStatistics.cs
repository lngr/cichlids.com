namespace Cichlids.Etl.Runtime;

/// <summary>
/// Counters for a single ETL step run: rows read from the legacy database, rows inserted or
/// updated in the target, and rows or field values skipped, each skip attributed to a reason so
/// the end-of-run report explains every anomaly instead of only a total.
/// </summary>
public sealed class StepStatistics
{
    private readonly Dictionary<string, int> _skipReasons = [];
    private readonly List<string> _warnings = [];

    public StepStatistics(string stepName)
    {
        StepName = stepName;
    }

    public string StepName { get; }
    public int Read { get; private set; }
    public int Inserted { get; private set; }
    public int Updated { get; private set; }
    public int Skipped => _skipReasons.Values.Sum();
    public IReadOnlyDictionary<string, int> SkipReasons => _skipReasons;
    public IReadOnlyList<string> Warnings => _warnings;

    public void AddRead(int count = 1) => Read += count;

    public void AddInserted(int count = 1) => Inserted += count;

    public void AddUpdated() => Updated++;

    public void AddSkip(string reason) =>
        _skipReasons[reason] = _skipReasons.GetValueOrDefault(reason) + 1;

    public void AddWarning(string message) => _warnings.Add(message);
}
