namespace Banking.Domain.Ledger;

/// <summary>Contas de sistema criadas pela migration do ledger. Os ids são fixos.</summary>
public static class SystemLedgerAccounts
{
    public static readonly Guid Funding = new("00000000-0000-0000-0000-000000010101");

    public static readonly Guid Settlement = new("00000000-0000-0000-0000-000000010102");

    public static readonly Guid Clearing = new("00000000-0000-0000-0000-000000020201");
}
