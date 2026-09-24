using Banking.Application.Common;

namespace Banking.Application.Reconciliation;

public sealed record ReconciliationFinding(string Check, string Detail);

public sealed record ReconciliationReport(DateTimeOffset RanAt, DateOnly StatementDate, IReadOnlyList<ReconciliationFinding> Findings)
{
    public bool IsConsistent => Findings.Count == 0;
}

/// <summary>Invariantes do ledger e confronto com o extrato do provider (docs/ledger/lancamentos.md).</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> RunAsync(DateOnly statementDate, CancellationToken cancellationToken);
}

public sealed class ReconciliationHandler(IReconciliationService reconciliation, TimeProvider time)
{
    public async Task<Result<ReconciliationReport>> RunAsync(Actor actor, DateOnly? statementDate, CancellationToken cancellationToken)
    {
        if (!actor.IsOperator && !actor.IsAdmin)
        {
            return Error.Forbidden("operator_required", "Só operador ou admin roda a reconciliação.");
        }

        var date = statementDate ?? Domain.Common.BusinessCalendar.DateOf(time.GetUtcNow());
        return await reconciliation.RunAsync(date, cancellationToken);
    }
}
