using Banking.Domain.Accounts;
using Banking.Domain.Common;
using Banking.Domain.Ledger;

namespace Banking.Domain.Payments;

public enum ExternalTransferStatus
{
    /// <summary>Recusada antes de reservar o valor (saldo, bloqueio, limite). Não tem lançamento.</summary>
    Rejected,

    /// <summary>Valor reservado em Clearing, ainda não enviado.</summary>
    Created,

    /// <summary>A ordem pode ter chegado ao provider. Só a consulta ao provider tira daqui (ADR-006).</summary>
    Unknown,

    Processing,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>
/// Cash-out pelo provider, em etapas (ADR-006). Toda transição acontece com a linha travada.
/// </summary>
public sealed class ExternalTransfer
{
    private ExternalTransfer()
    {
        CurrencyCode = string.Empty;
        DestinationBank = string.Empty;
        DestinationBranch = string.Empty;
        DestinationAccount = string.Empty;
        RequestedBy = string.Empty;
        IdempotencyKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SourceAccountId { get; private set; }

    public long AmountMinor { get; private set; }

    public string CurrencyCode { get; private set; }

    public string DestinationBank { get; private set; }

    public string DestinationBranch { get; private set; }

    public string DestinationAccount { get; private set; }

    public string RequestedBy { get; private set; }

    public string IdempotencyKey { get; private set; }

    public ExternalTransferStatus Status { get; private set; }

    public RejectionReason? RejectionReason { get; private set; }

    public string? FailureReason { get; private set; }

    public int SubmitAttempts { get; private set; }

    public DateTimeOffset? LastSubmittedAt { get; private set; }

    public DateTimeOffset? NextCheckAt { get; private set; }

    public bool RequiresManualReview { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Money Amount => Money.FromMinor(AmountMinor, Currency.FromCode(CurrencyCode));

    /// <summary>Chave de idempotência do lado do provider: o provider deduplica reenvios por ela.</summary>
    public string ClientReference => Id.ToString("N");

    public string ReservationExternalId => $"external-transfer:{Id}:reservation";

    /// <summary>Liquidação e estorno usam o mesmo id: o UNIQUE do ledger impede que os dois aconteçam.</summary>
    public string ResolutionExternalId => $"external-transfer:{Id}:resolution";

    public bool IsTerminal => Status is ExternalTransferStatus.Completed or ExternalTransferStatus.Failed
        or ExternalTransferStatus.Cancelled or ExternalTransferStatus.Rejected;

    public static (ExternalTransfer Transfer, LedgerTransaction? Reservation) Create(
        Account source,
        LedgerBalance sourceBalance,
        Money amount,
        ExternalDestination destination,
        string requestedBy,
        string idempotencyKey,
        Money usedToday,
        TransferLimits limits,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceBalance);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(limits);

        if (!amount.IsPositive)
        {
            throw new DomainException("invalid_amount", "Transferência precisa ter valor positivo.");
        }

        var transfer = new ExternalTransfer
        {
            Id = Guid.CreateVersion7(now),
            SourceAccountId = source.Id,
            AmountMinor = amount.MinorUnits,
            CurrencyCode = amount.Currency.Code,
            DestinationBank = destination.Bank,
            DestinationBranch = destination.Branch,
            DestinationAccount = destination.Account,
            RequestedBy = requestedBy,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var rejection = Evaluate(source, sourceBalance, amount, usedToday, limits);
        if (rejection is not null)
        {
            transfer.Status = ExternalTransferStatus.Rejected;
            transfer.RejectionReason = rejection;
            return (transfer, null);
        }

        var reservation = LedgerTransaction.Create(
            transfer.ReservationExternalId,
            LedgerTransactionType.ExternalTransferReservation,
            $"Transferência externa para {destination.Bank}/{destination.Branch}",
            now,
            [PostingLine.Debit(source.LedgerAccountId, amount), PostingLine.Credit(SystemLedgerAccounts.Clearing, amount)]);

        transfer.Status = ExternalTransferStatus.Created;
        return (transfer, reservation);
    }

    /// <summary>Gravado e commitado antes de chamar o provider: se o processo cair durante a chamada, o estado já diz a verdade.</summary>
    public void MarkSubmitting(DateTimeOffset now, TimeSpan checkAfter)
    {
        if (Status is not (ExternalTransferStatus.Created or ExternalTransferStatus.Unknown))
        {
            throw new DomainException("invalid_transition", $"Não é possível enviar a partir de {Status}.");
        }

        Status = ExternalTransferStatus.Unknown;
        SubmitAttempts++;
        LastSubmittedAt = now;
        NextCheckAt = now + checkAfter;
        UpdatedAt = now;
    }

    /// <summary>Aplica a resposta do envio. Recusa definitiva devolve o estorno a lançar.</summary>
    public LedgerTransaction? ApplySubmitResult(SubmitResult result, LedgerTransaction reservation, DateTimeOffset now, TimeSpan checkAfter)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Status != ExternalTransferStatus.Unknown)
        {
            return null;
        }

        UpdatedAt = now;
        switch (result.Outcome)
        {
            case SubmitOutcome.Accepted or SubmitOutcome.AlreadyExists:
                Status = ExternalTransferStatus.Processing;
                NextCheckAt = now + checkAfter;
                return null;
            case SubmitOutcome.Rejected:
                return Fail(result.Reason ?? "rejected_by_provider", reservation, now);
            default:
                NextCheckAt = now + checkAfter;
                return null;
        }
    }

    /// <summary>Aplica o status consultado no provider. Conclusão devolve a liquidação; falha devolve o estorno.</summary>
    public LedgerTransaction? ApplyProviderStatus(
        ProviderStatusResult status, LedgerTransaction reservation, DateTimeOffset now, TimeSpan checkAfter)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (Status is not (ExternalTransferStatus.Unknown or ExternalTransferStatus.Processing))
        {
            return null;
        }

        UpdatedAt = now;
        switch (status.Status)
        {
            case ProviderStatus.Completed:
                Status = ExternalTransferStatus.Completed;
                NextCheckAt = null;
                return LedgerTransaction.Create(
                    ResolutionExternalId,
                    LedgerTransactionType.ExternalTransferSettlement,
                    "Liquidação de transferência externa",
                    now,
                    [PostingLine.Debit(SystemLedgerAccounts.Clearing, Amount), PostingLine.Credit(SystemLedgerAccounts.Settlement, Amount)]);
            case ProviderStatus.Failed:
                return Fail(status.Reason ?? "failed_at_provider", reservation, now);
            case ProviderStatus.Processing:
                Status = ExternalTransferStatus.Processing;
                NextCheckAt = now + checkAfter;
                return null;
            default:
                NextCheckAt = now + checkAfter;
                return null;
        }
    }

    public LedgerTransaction Cancel(LedgerTransaction reservation, DateTimeOffset now)
    {
        if (Status != ExternalTransferStatus.Created)
        {
            throw new DomainException("not_cancellable", "Só dá para cancelar antes do envio ao provider.");
        }

        Status = ExternalTransferStatus.Cancelled;
        NextCheckAt = null;
        UpdatedAt = now;
        return Reverse(reservation, "Cancelamento de transferência externa", now);
    }

    /// <summary>Tentativas esgotadas sem resposta conclusiva. Continua UNKNOWN: só uma pessoa resolve.</summary>
    public void FlagForManualReview(DateTimeOffset now)
    {
        RequiresManualReview = true;
        NextCheckAt = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Desfecho confirmado fora do fluxo automático (ver <see cref="ManualResolution"/>). Só para transferência em revisão.
    /// </summary>
    public LedgerTransaction ResolveManually(ManualOutcome outcome, LedgerTransaction reservation, DateTimeOffset now)
    {
        if (!RequiresManualReview || IsTerminal)
        {
            throw new DomainException("not_in_manual_review", "A transferência não está esperando revisão manual.");
        }

        RequiresManualReview = false;
        UpdatedAt = now;
        if (outcome == ManualOutcome.Failed)
        {
            return Fail("manual_resolution", reservation, now);
        }

        Status = ExternalTransferStatus.Completed;
        NextCheckAt = null;
        return LedgerTransaction.Create(
            ResolutionExternalId,
            LedgerTransactionType.ExternalTransferSettlement,
            "Liquidação de transferência externa (resolução manual)",
            now,
            [PostingLine.Debit(SystemLedgerAccounts.Clearing, Amount), PostingLine.Credit(SystemLedgerAccounts.Settlement, Amount)]);
    }

    public void ScheduleCheck(DateTimeOffset at)
    {
        if (!IsTerminal)
        {
            NextCheckAt = at;
        }
    }

    private LedgerTransaction Fail(string reason, LedgerTransaction reservation, DateTimeOffset now)
    {
        Status = ExternalTransferStatus.Failed;
        FailureReason = reason;
        NextCheckAt = null;
        return Reverse(reservation, "Estorno de transferência externa", now);
    }

    private LedgerTransaction Reverse(LedgerTransaction reservation, string description, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (reservation.ExternalId != ReservationExternalId)
        {
            throw new InvalidOperationException("A reserva informada não é desta transferência.");
        }

        return reservation.Reverse(ResolutionExternalId, LedgerTransactionType.ExternalTransferReversal, description, now);
    }

    private static RejectionReason? Evaluate(Account source, LedgerBalance sourceBalance, Money amount, Money usedToday, TransferLimits limits)
    {
        if (amount.Currency != source.Currency)
        {
            return Common.RejectionReason.CurrencyMismatch;
        }

        if (source.RejectionForMovement() is { } sourceRejection)
        {
            return sourceRejection;
        }

        if (amount > limits.PerTransaction)
        {
            return Common.RejectionReason.LimitExceeded;
        }

        if (usedToday + amount > limits.DailyPerAccount)
        {
            return Common.RejectionReason.DailyLimitExceeded;
        }

        return sourceBalance.CanPost(EntryDirection.Debit, amount) ? null : Common.RejectionReason.InsufficientFunds;
    }
}

/// <summary>Conta de destino em outro banco. Sem documento do titular: não guardamos PII de terceiros.</summary>
public sealed record ExternalDestination(string Bank, string Branch, string Account)
{
    public static bool TryCreate(string? bank, string? branch, string? account, out ExternalDestination destination)
    {
        destination = null!;
        if (bank is not { Length: 8 } || !bank.All(char.IsAsciiDigit)
            || branch is not { Length: >= 1 and <= 4 } || !branch.All(char.IsAsciiDigit)
            || string.IsNullOrWhiteSpace(account) || account.Length > 30
            || !account.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return false;
        }

        destination = new ExternalDestination(bank, branch, account);
        return true;
    }
}
