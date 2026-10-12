using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Runtime;

/// <summary>
/// Runs ETL steps inside database transactions. A single step runs in a transaction of its own.
/// The full run checks the empty-target guard and runs every write step in one transaction, so a
/// failure anywhere leaves the target as empty as the guard found it and the run can start over.
/// </summary>
public static class EtlRunner
{
    public static async Task RunAsync(IEtlStep step, EtlContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Target.BeginTransactionAsync(cancellationToken);
        context.Db.Database.UseTransaction(transaction);
        context.Transaction = transaction;

        await step.RunAsync(context, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Runs the full migration: checks the guard, calls afterGuard (when given) once the guard has
    /// passed, runs the write steps in order and commits them together. The verify step runs
    /// after the commit in a transaction of its own. Returns 0 when the verification passes and 1
    /// when it reports a mismatch; the committed rows stay in both cases. Throws an
    /// EmptyTargetException before the first write when the guard fails.
    /// </summary>
    public static async Task<int> RunAllAsync(
        EtlContext context,
        IReadOnlyList<IEtlStep> writeSteps,
        IEtlStep verifyStep,
        Func<CancellationToken, Task>? afterGuard,
        CancellationToken cancellationToken)
    {
        await using (var transaction = await context.Target.BeginTransactionAsync(cancellationToken))
        {
            context.Db.Database.UseTransaction(transaction);
            context.Transaction = transaction;

            Console.WriteLine("Checking that the target database is empty...");
            await EmptyTargetGuard.EnsureEmptyAsync(context.Db, cancellationToken);

            if (afterGuard is not null)
            {
                await afterGuard(cancellationToken);
            }

            foreach (var step in writeSteps)
            {
                Console.WriteLine($"Running step '{step.Name}'...");
                await step.RunAsync(context, cancellationToken);
            }

            Console.WriteLine("Committing the full run...");
            await transaction.CommitAsync(cancellationToken);
        }

        Console.WriteLine($"Running step '{verifyStep.Name}'...");
        await RunAsync(verifyStep, context, cancellationToken);

        return context.VerificationReport is { Passed: false } ? 1 : 0;
    }
}
