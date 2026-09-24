using Banking.Application.Abstractions;
using Banking.Application.Audit;
using Banking.Application.Common;
using Banking.Application.Deposits;
using Banking.Application.ExternalTransfers;
using Banking.Application.Reversals;
using Banking.Contracts.Events;
using Banking.Domain.Common;
using Banking.Domain.Payments;

namespace Banking.Application.Operations;

public sealed record ManualResolutionView(
    Guid Id,
    Guid ExternalTransferId,
    string Outcome,
    string Evidence,
    string Status,
    string RequestedBy,
    string? DecidedBy,
    DateTimeOffset CreatedAt)
{
    public static ManualResolutionView From(ManualResolution resolution) => new(
        resolution.Id,
        resolution.ExternalTransferId,
        Codes.Of(resolution.Outcome),
        resolution.Evidence,
        Codes.Of(resolution.Status),
        resolution.RequestedBy,
        resolution.DecidedBy,
        resolution.CreatedAt);
}

/// <summary>Filas de trabalho do operador: o que espera uma decisão humana.</summary>
public sealed class OperatorQueuesHandler(
    IDepositRepository deposits,
    ITransferReversalRepository reversals,
    IExternalTransferRepository externalTransfers,
    IManualResolutionRepository resolutions)
{
    public const int QueueLimit = 100;

    public async Task<Result<IReadOnlyList<DepositView>>> PendingDepositsAsync(Actor actor, CancellationToken cancellationToken) =>
        actor.IsOperator
            ? Result<IReadOnlyList<DepositView>>.From(
                [.. (await deposits.ListByStatusAsync(DepositStatus.PendingApproval, QueueLimit, cancellationToken)).Select(DepositView.From)])
            : OperatorRequired();

    public async Task<Result<IReadOnlyList<ReversalView>>> PendingReversalsAsync(Actor actor, CancellationToken cancellationToken) =>
        actor.IsOperator
            ? Result<IReadOnlyList<ReversalView>>.From(
                [.. (await reversals.ListByStatusAsync(ReversalStatus.PendingApproval, QueueLimit, cancellationToken)).Select(ReversalView.From)])
            : OperatorRequired();

    public async Task<Result<IReadOnlyList<ExternalTransferView>>> InManualReviewAsync(Actor actor, CancellationToken cancellationToken) =>
        actor.IsOperator
            ? Result<IReadOnlyList<ExternalTransferView>>.From(
                [.. (await externalTransfers.ListNeedingReviewAsync(QueueLimit, cancellationToken)).Select(ExternalTransferView.From)])
            : OperatorRequired();

    public async Task<Result<IReadOnlyList<ManualResolutionView>>> PendingResolutionsAsync(Actor actor, CancellationToken cancellationToken) =>
        actor.IsOperator
            ? Result<IReadOnlyList<ManualResolutionView>>.From(
                [.. (await resolutions.ListByStatusAsync(ManualResolutionStatus.PendingApproval, QueueLimit, cancellationToken)).Select(ManualResolutionView.From)])
            : OperatorRequired();

    private static Error OperatorRequired() => Error.Forbidden("operator_required", "Só operador vê as filas de trabalho.");
}

/// <summary>Resolução manual de transferência externa em revisão, com maker-checker.</summary>
public sealed class ManualResolutionHandler(
    IUnitOfWork unitOfWork,
    IExternalTransferRepository transfers,
    IManualResolutionRepository resolutions,
    IAccountRepository accounts,
    ILedger ledger,
    IOutbox outbox,
    IAuditTrail audit,
    TimeProvider time)
{
    public async Task<Result<ManualResolutionView>> RequestAsync(
        Actor actor, Guid externalTransferId, string? outcome, string? evidence, CancellationToken cancellationToken)
    {
        if (!actor.IsOperator)
        {
            return Error.Forbidden("operator_required", "Só operador registra resolução manual.");
        }

        ManualOutcome parsed;
        switch (outcome)
        {
            case "completed":
                parsed = ManualOutcome.Completed;
                break;
            case "failed":
                parsed = ManualOutcome.Failed;
                break;
            default:
                return Error.Validation("invalid_outcome", "Desfecho deve ser completed ou failed.");
        }

        var transfer = await transfers.GetAsync(externalTransferId, cancellationToken);
        if (transfer is null)
        {
            return Error.NotFound("Transferência externa");
        }

        ManualResolution resolution;
        try
        {
            resolution = ManualResolution.Request(transfer, parsed, evidence ?? string.Empty, actor.Subject, time.GetUtcNow());
        }
        catch (DomainException error)
        {
            return error.Code == "invalid_evidence" ? Error.Validation(error.Code, error.Message) : Error.Conflict(error.Code, error.Message);
        }

        resolutions.Add(resolution);
        audit.Record(AuditEntry.Of(
            actor, "external-transfer.manual-resolution.request", "external_transfer", transfer.Id, "pending_approval",
            ("resolutionId", resolution.Id.ToString()), ("outcome", Codes.Of(parsed))));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return Error.Conflict("resolution_already_requested", "Já existe uma resolução pendente para esta transferência.");
        }

        return ManualResolutionView.From(resolution);
    }

    public async Task<Result<ManualResolutionView>> GetAsync(Actor actor, Guid id, CancellationToken cancellationToken) =>
        actor.IsOperator && await resolutions.GetAsync(id, cancellationToken) is { } resolution
            ? ManualResolutionView.From(resolution)
            : Error.NotFound("Resolução manual");

    public Task<Result<ManualResolutionView>> ApproveAsync(Actor actor, Guid id, CancellationToken cancellationToken) =>
        DecideAsync(actor, id, approve: true, cancellationToken);

    public Task<Result<ManualResolutionView>> RejectAsync(Actor actor, Guid id, CancellationToken cancellationToken) =>
        DecideAsync(actor, id, approve: false, cancellationToken);

    private async Task<Result<ManualResolutionView>> DecideAsync(Actor actor, Guid id, bool approve, CancellationToken cancellationToken)
    {
        if (!actor.IsOperator)
        {
            return Error.NotFound("Resolução manual");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var resolution = await resolutions.FindForUpdateAsync(id, cancellationToken);
        if (resolution is null)
        {
            return Error.NotFound("Resolução manual");
        }

        var transfer = await transfers.FindForUpdateAsync(resolution.ExternalTransferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transferência externa {resolution.ExternalTransferId} não encontrada.");
        var now = time.GetUtcNow();
        try
        {
            if (approve)
            {
                var reservation = await ledger.FindTransactionByExternalIdAsync(transfer.ReservationExternalId, cancellationToken)
                    ?? throw new InvalidOperationException("Reserva da transferência externa não encontrada.");
                var account = await accounts.FindAsync(transfer.SourceAccountId, cancellationToken)
                    ?? throw new InvalidOperationException("Conta de origem não encontrada.");
                await ledger.LockBalancesAsync([account.LedgerAccountId], cancellationToken);

                ledger.Add(resolution.Approve(actor.Subject, transfer, reservation, now));
                var amount = transfer.Amount.ToDecimalString();
                outbox.Enqueue(
                    transfer.Status == ExternalTransferStatus.Completed
                        ? new ExternalTransferCompletedV1(transfer.Id, transfer.SourceAccountId, amount, transfer.CurrencyCode)
                        : new ExternalTransferFailedV1(transfer.Id, transfer.SourceAccountId, amount, transfer.CurrencyCode, "manual_resolution"),
                    now);
            }
            else
            {
                resolution.Reject(actor.Subject, now);
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
            approve ? "external-transfer.manual-resolution.approve" : "external-transfer.manual-resolution.reject",
            "external_transfer",
            transfer.Id,
            approve ? $"applied:{Codes.Of(transfer.Status)}" : "rejected",
            ("resolutionId", resolution.Id.ToString()),
            ("requestedBy", resolution.RequestedBy)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ManualResolutionView.From(resolution);
    }
}
