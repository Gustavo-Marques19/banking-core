namespace Banking.Domain.Ledger;

public enum LedgerTransactionType
{
    Deposit,
    InternalTransfer,
    ExternalTransferReservation,
    ExternalTransferSettlement,
    ExternalTransferReversal,
    TransferReversal,
}
