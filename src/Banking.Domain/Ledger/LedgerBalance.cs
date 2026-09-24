using Banking.Domain.Common;

namespace Banking.Domain.Ledger;

/// <summary>Saldo materializado de uma conta, lido com lock antes de decidir uma operação (ADR-004).</summary>
public sealed record LedgerBalance(Guid LedgerAccountId, Money Balance, EntryDirection NormalBalance, bool AllowNegative)
{
    public Money BalanceAfter(EntryDirection direction, Money amount) =>
        direction == NormalBalance ? Balance + amount : Balance - amount;

    public bool CanPost(EntryDirection direction, Money amount) =>
        AllowNegative || !BalanceAfter(direction, amount).IsNegative;
}
