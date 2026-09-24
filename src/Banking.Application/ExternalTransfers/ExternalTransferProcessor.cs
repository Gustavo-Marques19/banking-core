using Banking.Application.Abstractions;
using Banking.Application.Audit;
using Banking.Application.Common;
using Banking.Contracts.Events;
using Banking.Domain.Ledger;
using Banking.Domain.Payments;

namespace Banking.Application.ExternalTransfers;

/// <summary>
/// Envio e reconciliação das transferências externas (ADR-006). O provider é sempre chamado fora de transação
/// de banco; o resultado é aplicado depois, com a linha travada.
/// </summary>
public sealed class ExternalTransferProcessor(
    IUnitOfWork unitOfWork,
    IExternalTransferRepository transfers,
    IAccountRepository accounts,
    ILedger ledger,
    IBankingProvider provider,
    IOutbox outbox,
    IAuditTrail audit,
    ExternalTransferSettings settings,
    TimeProvider time)
{
    /// <summary>Envia as transferências em CREATED. Devolve quantas foram enviadas.</summary>
    public async Task<int> SubmitPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        List<(Guid Id, ProviderTransferRequest Request)> claimed;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var pending = await transfers.LockCreatedAsync(batchSize, cancellationToken);
            var now = time.GetUtcNow();
            foreach (var transfer in pending)
            {
                transfer.MarkSubmitting(now, settings.CheckInterval);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            claimed = [.. pending.Select(t => (t.Id, RequestFor(t)))];
        }

        foreach (var (id, request) in claimed)
        {
            await SubmitAsync(id, request, cancellationToken);
        }

        return claimed.Count;
    }

    /// <summary>Consulta o provider para as transferências em UNKNOWN ou PROCESSING cuja verificação venceu.</summary>
    public async Task<int> ReconcileDueAsync(int batchSize, CancellationToken cancellationToken)
    {
        var due = await transfers.ListDueForCheckAsync(time.GetUtcNow(), batchSize, cancellationToken);
        foreach (var id in due)
        {
            await ReconcileAsync(id, cancellationToken);
        }

        return due.Count;
    }

    public async Task ReconcileAsync(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await transfers.GetAsync(id, cancellationToken);
        if (snapshot is null || snapshot.IsTerminal || snapshot.Status == ExternalTransferStatus.Created)
        {
            return;
        }

        ProviderStatusResult status;
        try
        {
            status = await provider.GetTransferStatusAsync(snapshot.ClientReference, cancellationToken);
        }
        catch (ProviderUnavailableException)
        {
            await ApplyAsync(id, (transfer, _, now) =>
            {
                transfer.ScheduleCheck(now + settings.CheckInterval);
                return null;
            }, cancellationToken);
            return;
        }

        if (status.Status == ProviderStatus.NotFound && snapshot.Status == ExternalTransferStatus.Unknown)
        {
            await ResubmitOrEscalateAsync(id, cancellationToken);
            return;
        }

        await ApplyAsync(id, (transfer, reservation, now) =>
            transfer.ApplyProviderStatus(status, reservation, now, settings.CheckInterval), cancellationToken);
    }

    /// <summary>
    /// O provider não conhece a ordem. Reenviar com a mesma referência é seguro porque ele deduplica;
    /// esgotadas as tentativas, a transferência continua UNKNOWN e vai para revisão manual.
    /// </summary>
    private async Task ResubmitOrEscalateAsync(Guid id, CancellationToken cancellationToken)
    {
        ProviderTransferRequest? request = null;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var transfer = await transfers.FindForUpdateAsync(id, cancellationToken);
            if (transfer is null || transfer.Status != ExternalTransferStatus.Unknown || transfer.RequiresManualReview)
            {
                return;
            }

            var now = time.GetUtcNow();
            if (transfer.SubmitAttempts >= settings.MaxSubmitAttempts)
            {
                transfer.FlagForManualReview(now);
                outbox.Enqueue(new ExternalTransferNeedsReviewV1(transfer.Id, transfer.SourceAccountId, transfer.SubmitAttempts), now);
                audit.Record(AuditEntry.Of(
                    Worker, "external-transfer.escalate", "external_transfer", transfer.Id, "needs_review",
                    ("submitAttempts", transfer.SubmitAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            }
            else
            {
                transfer.MarkSubmitting(now, settings.CheckInterval);
                request = RequestFor(transfer);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (request is not null)
        {
            await SubmitAsync(id, request, cancellationToken);
        }
    }

    private async Task SubmitAsync(Guid id, ProviderTransferRequest request, CancellationToken cancellationToken)
    {
        using var activity = BankingTelemetry.Source.StartActivity("external-transfer.submit");
        activity?.SetTag("banking.external_transfer.id", id);
        var result = await provider.SubmitTransferAsync(request, cancellationToken);
        activity?.SetTag("banking.provider.outcome", result.Outcome.ToString());
        if (result.Outcome == SubmitOutcome.Unknown)
        {
            BankingTelemetry.TransferUnknown.Add(1, new KeyValuePair<string, object?>("reason", result.Reason));
        }

        await ApplyAsync(id, (transfer, reservation, now) =>
            transfer.ApplySubmitResult(result, reservation, now, settings.CheckInterval), cancellationToken);
    }

    private async Task ApplyAsync(
        Guid id,
        Func<ExternalTransfer, LedgerTransaction, DateTimeOffset, LedgerTransaction?> transition,
        CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var transfer = await transfers.FindForUpdateAsync(id, cancellationToken);
        if (transfer is null)
        {
            return;
        }

        var reservation = await ledger.FindTransactionByExternalIdAsync(transfer.ReservationExternalId, cancellationToken)
            ?? throw new InvalidOperationException($"Transferência externa {id} sem reserva no ledger.");
        var account = await accounts.FindAsync(transfer.SourceAccountId, cancellationToken)
            ?? throw new InvalidOperationException($"Conta {transfer.SourceAccountId} não encontrada.");

        // Estorno credita o cliente: mesmo lock das outras operações na conta (ADR-004).
        await ledger.LockBalancesAsync([account.LedgerAccountId], cancellationToken);

        var before = transfer.Status;
        var now = time.GetUtcNow();
        var posting = transition(transfer, reservation, now);
        if (posting is not null)
        {
            ledger.Add(posting);
        }

        if (transfer.Status != before)
        {
            EnqueueStatusEvent(transfer, now);
            var status = Codes.Of(transfer.Status);
            audit.Record(AuditEntry.Of(
                Worker, "external-transfer.transition", "external_transfer", transfer.Id, $"{Codes.Of(before)}->{status}",
                ("failureReason", transfer.FailureReason)));
            BankingTelemetry.RecordTransfer("external", status, transfer.FailureReason);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void EnqueueStatusEvent(ExternalTransfer transfer, DateTimeOffset now)
    {
        var amount = transfer.Amount.ToDecimalString();
        object? integrationEvent = transfer.Status switch
        {
            ExternalTransferStatus.Completed => new ExternalTransferCompletedV1(transfer.Id, transfer.SourceAccountId, amount, transfer.CurrencyCode),
            ExternalTransferStatus.Failed => new ExternalTransferFailedV1(
                transfer.Id, transfer.SourceAccountId, amount, transfer.CurrencyCode, transfer.FailureReason ?? "failed"),
            _ => null,
        };

        if (integrationEvent is not null)
        {
            outbox.Enqueue(integrationEvent, now);
        }
    }

    private static readonly Actor Worker = Actor.System("external-transfer-worker");

    private static ProviderTransferRequest RequestFor(ExternalTransfer transfer) => new(
        transfer.ClientReference,
        transfer.Amount,
        new ExternalDestination(transfer.DestinationBank, transfer.DestinationBranch, transfer.DestinationAccount));
}
