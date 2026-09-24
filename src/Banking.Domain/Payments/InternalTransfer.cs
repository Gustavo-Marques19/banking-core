using Banking.Domain.Accounts;
using Banking.Domain.Common;
using Banking.Domain.Ledger;

namespace Banking.Domain.Payments;

public enum TransferStatus
{
    Completed,
    Rejected,
}

/// <summary>Transferência entre contas deste banco: uma transação de banco, sem provider (ADR-006).</summary>
public sealed class InternalTransfer
{
    private const int MaxDescriptionLength = 140;

    private InternalTransfer()
    {
        CurrencyCode = string.Empty;
        RequestedBy = string.Empty;
        IdempotencyKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SourceAccountId { get; private set; }

    public Guid DestinationAccountId { get; private set; }

    public long AmountMinor { get; private set; }

    public string CurrencyCode { get; private set; }

    public string? Description { get; private set; }

    public string RequestedBy { get; private set; }

    public string IdempotencyKey { get; private set; }

    public TransferStatus Status { get; private set; }

    public RejectionReason? RejectionReason { get; private set; }

    public Guid? LedgerTransactionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Money Amount => Money.FromMinor(AmountMinor, Currency.FromCode(CurrencyCode));

    /// <summary>
    /// Decide com as duas linhas de saldo e o uso diário já travados (ADR-004). Recusa também é gravada.
    /// </summary>
    public static (InternalTransfer Transfer, LedgerTransaction? Posting) Decide(
        Account source,
        Account destination,
        LedgerBalance sourceBalance,
        Money amount,
        string? description,
        string requestedBy,
        string idempotencyKey,
        Money usedToday,
        TransferLimits limits,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sourceBalance);
        ArgumentNullException.ThrowIfNull(limits);

        if (source.Id == destination.Id)
        {
            throw new DomainException("same_account", "Origem e destino são a mesma conta.");
        }

        if (!amount.IsPositive)
        {
            throw new DomainException("invalid_amount", "Transferência precisa ter valor positivo.");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription?.Length > MaxDescriptionLength)
        {
            throw new DomainException("invalid_description", $"Descrição acima de {MaxDescriptionLength} caracteres.");
        }

        var transfer = new InternalTransfer
        {
            Id = Guid.CreateVersion7(now),
            SourceAccountId = source.Id,
            DestinationAccountId = destination.Id,
            AmountMinor = amount.MinorUnits,
            CurrencyCode = amount.Currency.Code,
            Description = trimmedDescription,
            RequestedBy = requestedBy,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
        };

        var rejection = Evaluate(source, destination, sourceBalance, amount, usedToday, limits);
        if (rejection is not null)
        {
            transfer.Status = TransferStatus.Rejected;
            transfer.RejectionReason = rejection;
            return (transfer, null);
        }

        var posting = LedgerTransaction.Create(
            $"transfer:{transfer.Id}",
            LedgerTransactionType.InternalTransfer,
            trimmedDescription ?? $"Transferência {source.Number} para {destination.Number}",
            now,
            [PostingLine.Debit(source.LedgerAccountId, amount), PostingLine.Credit(destination.LedgerAccountId, amount)]);

        transfer.Status = TransferStatus.Completed;
        transfer.LedgerTransactionId = posting.Id;
        return (transfer, posting);
    }

    private static RejectionReason? Evaluate(
        Account source, Account destination, LedgerBalance sourceBalance, Money amount, Money usedToday, TransferLimits limits)
    {
        if (amount.Currency != source.Currency || amount.Currency != destination.Currency)
        {
            return Common.RejectionReason.CurrencyMismatch;
        }

        if (source.RejectionForMovement() is { } sourceRejection)
        {
            return sourceRejection;
        }

        if (destination.RejectionForMovement() is not null)
        {
            return Common.RejectionReason.DestinationUnavailable;
        }

        if (amount > limits.PerTransaction)
        {
            return Common.RejectionReason.LimitExceeded;
        }

        if (usedToday + amount > limits.DailyPerAccount)
        {
            return Common.RejectionReason.DailyLimitExceeded;
        }

        if (!sourceBalance.CanPost(EntryDirection.Debit, amount))
        {
            return Common.RejectionReason.InsufficientFunds;
        }

        return null;
    }
}
