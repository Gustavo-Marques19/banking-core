using Banking.Application.Abstractions;
using Banking.Application.Audit;
using Banking.Application.Common;
using Banking.Contracts.Events;
using Banking.Domain.Common;
using Banking.Domain.Payments;

namespace Banking.Application.Reversals;

public sealed record ReversalView(
    Guid Id,
    Guid TransferId,
    string Reason,
    string Status,
    string? RejectionReason,
    string RequestedBy,
    string? DecidedBy,
    Guid? LedgerTransactionId,
    DateTimeOffset CreatedAt)
{
    public static ReversalView From(TransferReversal reversal) => new(
        reversal.Id,
        reversal.TransferId,
        reversal.Reason,
        Codes.Of(reversal.Status),
        Codes.Of(reversal.RejectionReason),
        reversal.RequestedBy,
        reversal.DecidedBy,
        reversal.LedgerTransactionId,
        reversal.CreatedAt);
}

/// <summary>Estorno de transferência interna com maker-checker. Tudo aqui é só para operador.</summary>
public sealed class ReversalHandler(
    IUnitOfWork unitOfWork,
    ITransferRepository transfers,
    ITransferReversalRepository reversals,
    IAccountRepository accounts,
    ILedger ledger,
    IOutbox outbox,
    IAuditTrail audit,
    TimeProvider time)
{
    public async Task<Result<ReversalView>> RequestAsync(Actor actor, Guid transferId, string? reason, CancellationToken cancellationToken)
    {
        if (!actor.IsOperator)
        {
            return Error.NotFound("Transferência");
        }

        var transfer = await transfers.FindAsync(transferId, cancellationToken);
        if (transfer is null)
        {
            return Error.NotFound("Transferência");
        }

        TransferReversal reversal;
        try
        {
            reversal = TransferReversal.Request(transfer, actor.Subject, reason ?? string.Empty, time.GetUtcNow());
        }
        catch (DomainException error)
        {
            return error.Code == "invalid_reason" ? Error.Validation(error.Code, error.Message) : Error.Conflict(error.Code, error.Message);
        }

        reversals.Add(reversal);
        audit.Record(AuditEntry.Of(actor, "transfer.reversal.request", "transfer", transfer.Id, "pending_approval", ("reversalId", reversal.Id.ToString())));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return Error.Conflict("reversal_already_requested", "Já existe um estorno pendente ou concluído para esta transferência.");
        }

        return ReversalView.From(reversal);
    }

    public async Task<Result<ReversalView>> GetAsync(Actor actor, Guid reversalId, CancellationToken cancellationToken) =>
        actor.IsOperator && await reversals.GetAsync(reversalId, cancellationToken) is { } reversal
            ? ReversalView.From(reversal)
            : Error.NotFound("Estorno");

    public Task<Result<ReversalView>> ApproveAsync(Actor actor, Guid reversalId, CancellationToken cancellationToken) =>
        DecideAsync(actor, reversalId, approve: true, cancellationToken);

    public Task<Result<ReversalView>> RejectAsync(Actor actor, Guid reversalId, CancellationToken cancellationToken) =>
        DecideAsync(actor, reversalId, approve: false, cancellationToken);

    private async Task<Result<ReversalView>> DecideAsync(Actor actor, Guid reversalId, bool approve, CancellationToken cancellationToken)
    {
        if (!actor.IsOperator)
        {
            return Error.NotFound("Estorno");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var reversal = await reversals.FindForUpdateAsync(reversalId, cancellationToken);
        if (reversal is null)
        {
            return Error.NotFound("Estorno");
        }

        var transfer = await transfers.FindAsync(reversal.TransferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transferência {reversal.TransferId} não encontrada.");
        var source = await accounts.FindAsync(transfer.SourceAccountId, cancellationToken)
            ?? throw new InvalidOperationException("Conta de origem não encontrada.");
        var destination = await accounts.FindAsync(transfer.DestinationAccountId, cancellationToken)
            ?? throw new InvalidOperationException("Conta de destino não encontrada.");

        var now = time.GetUtcNow();
        try
        {
            if (approve)
            {
                var original = await ledger.FindTransactionAsync(transfer.LedgerTransactionId!.Value, cancellationToken)
                    ?? throw new InvalidOperationException("Transação contábil da transferência não encontrada.");
                var balances = await ledger.LockBalancesAsync([source.LedgerAccountId, destination.LedgerAccountId], cancellationToken);
                var posting = reversal.Approve(actor.Subject, original, balances[destination.LedgerAccountId], destination.LedgerAccountId, now);
                if (posting is not null)
                {
                    ledger.Add(posting);
                    outbox.Enqueue(
                        new TransferReversedV1(transfer.Id, reversal.Id, source.Id, destination.Id, transfer.Amount.ToDecimalString(), transfer.CurrencyCode),
                        now);
                }
            }
            else
            {
                reversal.Reject(actor.Subject, now);
            }
        }
        catch (DomainException error) when (error.Code == "self_approval_not_allowed")
        {
            return Error.Forbidden(error.Code, error.Message);
        }
        catch (DomainException error)
        {
            return Error.Conflict(error.Code, error.Message);
        }

        audit.Record(AuditEntry.Of(
            actor,
            approve ? "transfer.reversal.approve" : "transfer.reversal.reject",
            "transfer",
            transfer.Id,
            reversal.RejectionReason is { } reason ? $"{Codes.Of(reversal.Status)}:{Codes.Of(reason)}" : Codes.Of(reversal.Status),
            ("reversalId", reversal.Id.ToString()),
            ("requestedBy", reversal.RequestedBy)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReversalView.From(reversal);
    }
}
