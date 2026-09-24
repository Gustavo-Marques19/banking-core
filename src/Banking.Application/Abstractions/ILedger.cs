using Banking.Domain.Ledger;

namespace Banking.Application.Abstractions;

public interface ILedger
{
    /// <summary>
    /// Trava as linhas de saldo em ordem de id até o fim da transação (ADR-004).
    /// Contas sem saldo materializado (as de sistema) não aparecem no resultado.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, LedgerBalance>> LockBalancesAsync(
        IReadOnlyCollection<Guid> ledgerAccountIds, CancellationToken cancellationToken);

    void Add(LedgerAccount account);

    void Add(LedgerTransaction transaction);

    Task<LedgerTransaction?> FindTransactionAsync(Guid id, CancellationToken cancellationToken);

    Task<LedgerTransaction?> FindTransactionByExternalIdAsync(string externalId, CancellationToken cancellationToken);
}
