namespace Cichlids.Etl.Runtime;

/// <summary>
/// Collects the <see cref="StepStatistics"/> of every step executed in one ETL run, keyed by
/// step name, so a run touching several steps reports each of them independently.
/// </summary>
public sealed class EtlStatistics
{
    private readonly Dictionary<string, StepStatistics> _steps = [];

    public StepStatistics ForStep(string stepName)
    {
        if (!_steps.TryGetValue(stepName, out var stats))
        {
            stats = new StepStatistics(stepName);
            _steps[stepName] = stats;
        }

        return stats;
    }

    public IReadOnlyCollection<StepStatistics> Steps => _steps.Values;
}
