namespace Cichlids.Etl.Runtime;

/// <summary>
/// The full set of rows one verification run produced, in the order the checks ran.
/// </summary>
public sealed class VerificationReport
{
    private readonly List<VerificationRow> _rows = [];

    public IReadOnlyList<VerificationRow> Rows => _rows;

    public void Add(VerificationRow row) => _rows.Add(row);

    /// <summary>
    /// True when every row is ok or purely informative; false as soon as one row is a mismatch.
    /// </summary>
    public bool Passed => _rows.All(row => row.Status != VerificationStatus.Mismatch);

    public int MismatchCount => _rows.Count(row => row.Status == VerificationStatus.Mismatch);
}
