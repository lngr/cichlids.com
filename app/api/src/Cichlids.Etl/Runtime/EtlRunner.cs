using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Runtime;

/// <summary>
/// Runs one step inside its own database transaction and commits it when the step completes, so
/// a failing step leaves none of its writes behind.
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
}
