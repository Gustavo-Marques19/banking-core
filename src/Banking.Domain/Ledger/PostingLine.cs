using Banking.Domain.Common;

namespace Banking.Domain.Ledger;

public sealed record PostingLine(Guid LedgerAccountId, EntryDirection Direction, Money Amount)
{
    public static PostingLine Debit(Guid ledgerAccountId, Money amount) => new(ledgerAccountId, EntryDirection.Debit, amount);

    public static PostingLine Credit(Guid ledgerAccountId, Money amount) => new(ledgerAccountId, EntryDirection.Credit, amount);
}
