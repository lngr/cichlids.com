// Story: task-3.32
using System.Text.Json;
using Cichlids.Etl.Runtime;

namespace Cichlids.Etl.Tests;

public sealed class VerificationReportPrinterTests
{
    [Fact]
    public async Task WritesTheRuleOfEveryRowIntoTheJson()
    {
        var report = new VerificationReport();
        report.Add(VerificationRow.Compare("post", 2, 2, "Non-deleted gallery pictures become posts."));
        report.Add(VerificationRow.Info("collection_skipped_empty", 1, "Empty galleries are skipped."));

        var path = Path.Combine(Path.GetTempPath(), $"verify-{Guid.NewGuid():N}.json");
        try
        {
            await VerificationReportPrinter.WriteJsonAsync(report, path, CancellationToken.None);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var rows = document.RootElement.GetProperty("rows").EnumerateArray().ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal("Non-deleted gallery pictures become posts.", rows[0].GetProperty("rule").GetString());
            Assert.Equal("Empty galleries are skipped.", rows[1].GetProperty("rule").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsARowWithoutARule(string rule)
    {
        Assert.Throws<ArgumentException>(() => VerificationRow.Compare("post", 1, 1, rule));
        Assert.Throws<ArgumentException>(() => VerificationRow.Info("post_state_draft", 1, rule));
    }
}
