using Banking.Domain.Common;
using Banking.Domain.Ledger;

namespace Banking.Domain.Payments;

public enum ManualOutcome
{
    Completed,
    Failed,
}

public enum ManualResolutionStatus
{
    PendingApproval,
    Applied,
    Rejected,
}

/// <summary>
/// Desfecho de uma transferência externa que esgotou as consultas automáticas. Um operador registra o que confirmou
/// com o provider, por outro canal, e a evidência; outro operador aprova. Só então o dinheiro se move.
/// </summary>
public sealed class ManualResolution
{
    private const int MaxEvidenceLength = 500;

    private ManualResolution()
    {
        Evidence = string.Empty;
        RequestedBy = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ExternalTransferId { get; private set; }

    public ManualOutcome Outcome { get; private set; }

    public string Evidence { get; private set; }

    public string RequestedBy { get; private set; }

    public ManualResolutionStatus Status { get; private set; }

    public string? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ManualResolution Request(
        ExternalTransfer transfer, ManualOutcome outcome, string evidence, string requestedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        if (!transfer.RequiresManualReview || transfer.IsTerminal)
        {
            throw new DomainException("not_in_manual_review", "A transferência não está esperando revisão manual.");
        }

        var trimmed = evidence?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxEvidenceLength)
        {
            throw new DomainException("invalid_evidence", $"Evidência precisa ter entre 1 e {MaxEvidenceLength} caracteres.");
        }

        return new ManualResolution
        {
            Id = Guid.CreateVersion7(now),
            ExternalTransferId = transfer.Id,
            Outcome = outcome,
            Evidence = trimmed,
            RequestedBy = requestedBy,
            Status = ManualResolutionStatus.PendingApproval,
            CreatedAt = now,
        };
    }

    /// <summary>Aprova e aplica: devolve a liquidação ou o estorno a lançar.</summary>
    public LedgerTransaction Approve(string approver, ExternalTransfer transfer, LedgerTransaction reservation, DateTimeOffset now)
    {
        EnsurePendingAndIndependent(approver);
        ArgumentNullException.ThrowIfNull(transfer);
        if (transfer.Id != ExternalTransferId)
        {
            throw new InvalidOperationException("A transferência informada não é a desta resolução.");
        }

        var posting = transfer.ResolveManually(Outcome, reservation, now);
        Status = ManualResolutionStatus.Applied;
        DecidedBy = approver;
        DecidedAt = now;
        return posting;
    }

    public void Reject(string approver, DateTimeOffset now)
    {
        EnsurePendingAndIndependent(approver);
        Status = ManualResolutionStatus.Rejected;
        DecidedBy = approver;
        DecidedAt = now;
    }

    private void EnsurePendingAndIndependent(string approver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approver);
        if (Status != ManualResolutionStatus.PendingApproval)
        {
            throw new DomainException("not_pending", "A resolução não está esperando aprovação.");
        }

        if (approver == RequestedBy)
        {
            throw new DomainException("self_approval_not_allowed", "Quem registrou a resolução não pode aprová-la.");
        }
    }
}
