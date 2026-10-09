namespace Cichlids.Etl.Runtime;

/// <summary>
/// Renders the statistics of a run as structured console output: one block per step with its
/// counters, skip reasons and warnings, so a run's outcome is legible without a separate log file.
/// </summary>
public static class ConsoleReport
{
    public static void Print(EtlStatistics statistics, bool dryRun)
    {
        Console.WriteLine();
        Console.WriteLine(dryRun ? "=== ETL run summary (dry run, no changes committed) ===" : "=== ETL run summary ===");

        foreach (var step in statistics.Steps)
        {
            Console.WriteLine();
            Console.WriteLine($"[{step.StepName}]");
            Console.WriteLine($"  read:      {step.Read}");
            Console.WriteLine($"  inserted:  {step.Inserted}");
            Console.WriteLine($"  updated:   {step.Updated}");
            Console.WriteLine($"  skipped:   {step.Skipped}");

            foreach (var (reason, count) in step.SkipReasons.OrderByDescending(r => r.Value))
            {
                Console.WriteLine($"    - {reason}: {count}");
            }

            if (step.Warnings.Count > 0)
            {
                Console.WriteLine($"  warnings:  {step.Warnings.Count}");
                foreach (var warning in step.Warnings)
                {
                    Console.WriteLine($"    - {warning}");
                }
            }
        }

        Console.WriteLine();
    }
}
