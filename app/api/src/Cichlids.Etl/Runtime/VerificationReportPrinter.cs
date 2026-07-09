using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cichlids.Etl.Runtime;

/// <summary>
/// Renders a verification report as an aligned console table and, on request, as JSON: the table
/// is the day-to-day read-out, the JSON is for diffing a run's outcome against a previous one or
/// feeding it into other tooling.
/// </summary>
public static class VerificationReportPrinter
{
    public static void PrintTable(VerificationReport report)
    {
        var entityWidth = Math.Max("entity".Length, report.Rows.Max(r => r.Entity.Length));

        Console.WriteLine();
        Console.WriteLine("=== ETL verification report ===");
        Console.WriteLine();
        Console.WriteLine(
            $"{"entity".PadRight(entityWidth)}  {"expected",12}  {"actual",12}  {"delta",8}  status");

        foreach (var row in report.Rows)
        {
            var status = row.Status switch
            {
                VerificationStatus.Ok => "ok",
                VerificationStatus.Mismatch => "mismatch",
                VerificationStatus.Info => "info",
                _ => row.Status.ToString(),
            };

            Console.WriteLine(
                $"{row.Entity.PadRight(entityWidth)}  {row.Expected,12}  {row.Actual,12}  {row.Delta,8}  {status}"
                + (row.Note is null ? string.Empty : $"  ({row.Note})"));
        }

        Console.WriteLine();
        Console.WriteLine(
            report.Passed
                ? "Result: ok (no mismatches)"
                : $"Result: {report.MismatchCount} mismatch(es)");
        Console.WriteLine();
    }

    public static async Task WriteJsonAsync(VerificationReport report, string path, CancellationToken cancellationToken)
    {
        var document = new JsonDocumentDto(
            DateTimeOffset.UtcNow,
            report.Passed,
            report.MismatchCount,
            report.Rows
                .Select(row => new JsonRowDto(row.Entity, row.Expected, row.Actual, row.Delta, row.Status.ToString().ToLowerInvariant(), row.Note))
                .ToList());

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(document, options);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private sealed record JsonRowDto(
        string Entity, long Expected, long Actual, long Delta, string Status, string? Note);

    private sealed record JsonDocumentDto(
        [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
        [property: JsonPropertyName("passed")] bool Passed,
        [property: JsonPropertyName("mismatchCount")] int MismatchCount,
        [property: JsonPropertyName("rows")] IReadOnlyList<JsonRowDto> Rows);
}
