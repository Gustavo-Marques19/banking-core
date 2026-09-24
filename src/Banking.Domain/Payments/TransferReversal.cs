using Banking.Domain.Common;
using Banking.Domain.Ledger;

namespace Banking.Domain.Payments;

public enum ReversalStatus
{
    PendingApproval,
    Completed,
    Rejected,
}

/// <summary>
/// Estorno de transferência interna, pedido por um operador e aprovado por outro (maker-checker, threat model T4).
/// O estorno é uma transação nova ligada à original; a original não muda.
/// </summary>
public sealed class TransferReversal
{
    private const int MaxReasonLength = 200;

    private TransferReversal()
    {
        RequestedBy = string.Empty;
        Reason = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid TransferId { get; private set; }

    public string RequestedBy { get; private set; }

    public string Reason { get; private set; }

    public ReversalStatus Status { get; private set; }

    public RejectionReason? RejectionReason { get; private set; }

    public string? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public Guid? LedgerTransactionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static TransferReversal Request(InternalTransfer transfer, string requestedBy, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        if (transfer.Status != TransferStatus.Completed)
        {
            throw new DomainException("not_reversible", "Só transferência concluída pode ser estornada.");
        }

        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxReasonLength)
        {
            throw new DomainException("invalid_reason", $"Motivo precisa ter entre 1 e {MaxReasonLength} caracteres.");
        }

        return new TransferReversal
        {
            Id = Guid.CreateVersion7(now),
            TransferId = transfer.Id,
            RequestedBy = requestedBy,
            Reason = trimmed,
            Status = ReversalStatus.PendingApproval,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Debita quem recebeu e credita quem enviou. Se o destinatário já gastou o dinheiro, o estorno é recusado:
    /// nunca deixa conta de cliente negativa.
    /// </summary>
    public LedgerTransaction? Approve(
        string approver, LedgerTransaction original, LedgerBalance destinationBalance, Guid destinationLedgerAccountId, DateTimeOffset now)
    {
        EnsurePendingAndIndependent(approver);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(destinationBalance);

        DecidedBy = approver;
        DecidedAt = now;
        var amount = original.Entries.Single(e => e.LedgerAccountId == destinationLedgerAccountId).Amount;
        if (!destinationBalance.CanPost(EntryDirection.Debit, amount))
        {
            Status = ReversalStatus.Rejected;
            RejectionReason = Common.RejectionReason.InsufficientFunds;
            return null;
        }

        var reversal = original.Reverse($"transfer:{TransferId}:reversal", LedgerTransactionType.TransferReversal, $"Estorno: {Reason}", now);
        Status = ReversalStatus.Completed;
        LedgerTransactionId = reversal.Id;
        return reversal;
    }

    public void Reject(string approver, DateTimeOffset now)
    {
        EnsurePendingAndIndependent(approver);
        DecidedBy = approver;
        DecidedAt = now;
        Status = ReversalStatus.Rejected;
        RejectionReason = Common.RejectionReason.RejectedByApprover;
    }

    private void EnsurePendingAndIndependent(string approver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approver);
        if (Status != ReversalStatus.PendingApproval)
        {
            throw new DomainException("not_pending", "O estorno não está esperando aprovação.");
        }

        if (approver == RequestedBy)
        {
            throw new DomainException("self_approval_not_allowed", "Quem pediu o estorno não pode aprová-lo.");
        }
    }
}
