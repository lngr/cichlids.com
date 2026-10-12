namespace Cichlids.Etl.Runtime;

/// <summary>
/// Outcome of comparing one migrated entity's independently recomputed expected count against
/// its actual count in the target database. Ok and mismatch rows carry a real expected value the
/// actual value is checked against; info rows have no independent expectation of their own
/// (expected is set equal to actual so the row still prints a value) and exist purely to surface a
/// number for a human to read.
/// </summary>
public enum VerificationStatus
{
    Ok,
    Mismatch,
    Info,
}

/// <summary>
/// One line of the verification report: a named check, its expected and actual counts, the
/// status that follows from comparing them, and the migration rule the expectation follows from.
/// Every row names a non-empty rule.
/// </summary>
public sealed record VerificationRow(
    string Entity, long Expected, long Actual, VerificationStatus Status, string Rule, string? Note = null)
{
    public string Rule { get; } = string.IsNullOrWhiteSpace(Rule)
        ? throw new ArgumentException($"The verification row '{Entity}' needs a rule.", nameof(Rule))
        : Rule;

    public long Delta => Actual - Expected;

    /// <summary>
    /// Builds a row whose status follows from comparing expected against actual: ok when they
    /// match, mismatch otherwise.
    /// </summary>
    public static VerificationRow Compare(string entity, long expected, long actual, string rule, string? note = null) =>
        new(entity, expected, actual, expected == actual ? VerificationStatus.Ok : VerificationStatus.Mismatch, rule, note);

    /// <summary>
    /// Builds an informative row that reports a count with no independently computed expectation
    /// to check it against.
    /// </summary>
    public static VerificationRow Info(string entity, long value, string rule, string? note = null) =>
        new(entity, value, value, VerificationStatus.Info, rule, note);
}
