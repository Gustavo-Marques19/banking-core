using Banking.Domain.Common;

namespace Banking.Domain.Ledger;

public sealed class LedgerEntry
{
    private LedgerEntry()
    {
        CurrencyCode = string.Empty;
    }

    internal LedgerEntry(Guid id, Guid transactionId, PostingLine line)
    {
        Id = id;
        TransactionId = transactionId;
        LedgerAccountId = line.LedgerAccountId;
        Direction = line.Direction;
        AmountMinor = line.Amount.MinorUnits;
        CurrencyCode = line.Amount.Currency.Code;
    }

    public Guid Id { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid LedgerAccountId { get; private set; }

    public EntryDirection Direction { get; private set; }

    public long AmountMinor { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>Preenchido pelo banco, só em contas com saldo materializado.</summary>
    public long? AccountSequence { get; private set; }

    /// <summary>Preenchido pelo banco, só em contas com saldo materializado.</summary>
    public long? BalanceAfterMinor { get; private set; }

    public Money Amount => Money.FromMinor(AmountMinor, Currency.FromCode(CurrencyCode));
}
