using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Runtime;

/// <summary>
/// Runs one step inside its own database transaction so a dry run can execute every read and
/// write exactly as a real run would, then roll back instead of committing: statistics reflect
/// the real outcome without leaving any row behind.
/// </summary>
public static class EtlRunner
{
    public static async Task RunAsync(IEtlStep step, EtlContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Target.BeginTransactionAsync(cancellationToken);
        context.Db.Database.UseTransaction(transaction);
        context.Transaction = transaction;

        await step.RunAsync(context, cancellationToken);

        if (context.DryRun)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        else
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
