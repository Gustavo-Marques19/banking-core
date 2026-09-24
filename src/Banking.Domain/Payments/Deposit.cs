using Banking.Domain.Accounts;
using Banking.Domain.Common;
using Banking.Domain.Ledger;

namespace Banking.Domain.Payments;

/// <summary>Entrada de dinheiro fictício feita por operador: D Funding / C Cliente.</summary>
public sealed class Deposit
{
    private const int MaxReasonLength = 200;

    private Deposit()
    {
        CurrencyCode = string.Empty;
        Reason = string.Empty;
        RequestedBy = string.Empty;
        IdempotencyKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public long AmountMinor { get; private set; }

    public string CurrencyCode { get; private set; }

    public string Reason { get; private set; }

    public string RequestedBy { get; private set; }

    public string IdempotencyKey { get; private set; }

    public DepositStatus Status { get; private set; }

    public RejectionReason? RejectionReason { get; private set; }

    public Guid? LedgerTransactionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Money Amount => Money.FromMinor(AmountMinor, Currency.FromCode(CurrencyCode));

    /// <summary>
    /// Decide o depósito com o saldo e o uso diário já travados. Concluído devolve a transação contábil a lançar.
    /// </summary>
    public static (Deposit Deposit, LedgerTransaction? Posting) Decide(
        Account account,
        Money amount,
        string reason,
        string requestedBy,
        string idempotencyKey,
        Money operatorUsedToday,
        DepositLimits limits,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length is 0 or > MaxReasonLength)
        {
            throw new DomainException("invalid_reason", $"Motivo precisa ter entre 1 e {MaxReasonLength} caracteres.");
        }

        if (!amount.IsPositive)
        {
            throw new DomainException("invalid_amount", "Depósito precisa ter valor positivo.");
        }

        var deposit = new Deposit
        {
            Id = Guid.CreateVersion7(now),
            AccountId = account.Id,
            AmountMinor = amount.MinorUnits,
            CurrencyCode = amount.Currency.Code,
            Reason = trimmedReason,
            RequestedBy = requestedBy,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
        };

        var rejection = Evaluate(account, amount, operatorUsedToday, limits);
        if (rejection is not null)
        {
            deposit.Status = DepositStatus.Rejected;
            deposit.RejectionReason = rejection;
            return (deposit, null);
        }

        var posting = LedgerTransaction.Create(
            $"deposit:{deposit.Id}",
            LedgerTransactionType.Deposit,
            $"Depósito na conta {account.Number}",
            now,
            [PostingLine.Debit(SystemLedgerAccounts.Funding, amount), PostingLine.Credit(account.LedgerAccountId, amount)]);

        deposit.Status = DepositStatus.Completed;
        deposit.LedgerTransactionId = posting.Id;
        return (deposit, posting);
    }

    private static RejectionReason? Evaluate(Account account, Money amount, Money operatorUsedToday, DepositLimits limits)
    {
        if (amount.Currency != account.Currency)
        {
            return Common.RejectionReason.CurrencyMismatch;
        }

        if (account.RejectionForMovement() is { } accountRejection)
        {
            return accountRejection;
        }

        if (amount > limits.PerOperation)
        {
            return Common.RejectionReason.LimitExceeded;
        }

        if (operatorUsedToday + amount > limits.DailyPerOperator)
        {
            return Common.RejectionReason.DailyLimitExceeded;
        }

        return null;
    }
}
