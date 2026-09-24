using Banking.Application.Abstractions;
using Banking.Application.Accounts;
using Banking.Application.Audit;
using Banking.Application.Common;
using Banking.Application.Idempotency;
using Banking.Contracts.Events;
using Banking.Domain.Common;
using Banking.Domain.Payments;

namespace Banking.Application.ExternalTransfers;

public sealed record ExternalDestinationView(string Bank, string Branch, string Account);

public sealed record ExternalTransferView(
    Guid Id,
    Guid SourceAccountId,
    string Amount,
    string Currency,
    ExternalDestinationView Destination,
    string Status,
    string? RejectionReason,
    string? FailureReason,
    bool RequiresManualReview,
    int SubmitAttempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ExternalTransferView From(ExternalTransfer transfer) => new(
        transfer.Id,
        transfer.SourceAccountId,
        transfer.Amount.ToDecimalString(),
        transfer.CurrencyCode,
        new ExternalDestinationView(transfer.DestinationBank, transfer.DestinationBranch, transfer.DestinationAccount),
        Codes.Of(transfer.Status),
        Codes.Of(transfer.RejectionReason),
        transfer.FailureReason,
        transfer.RequiresManualReview,
        transfer.SubmitAttempts,
        transfer.CreatedAt,
        transfer.UpdatedAt);
}

/// <summary>Intervalo entre consultas ao provider e limite de reenvios antes da revisão manual.</summary>
public sealed record ExternalTransferSettings(TimeSpan CheckInterval, int MaxSubmitAttempts);

public sealed record CreateExternalTransferCommand(
    Actor Actor,
    Guid SourceAccountId,
    string? Amount,
    string? Currency,
    string? DestinationBank,
    string? DestinationBranch,
    string? DestinationAccount,
    string? IdempotencyKey);

public sealed class CreateExternalTransferHandler(
    IdempotentExecutor executor,
    AccountAccess access,
    IAccountRepository accounts,
    IExternalTransferRepository transfers,
    ILedger ledger,
    ILimitUsageStore limitUsage,
    IOutbox outbox,
    IAuditTrail audit,
    TransferLimits limits,
    TimeProvider time)
{
    public const string Operation = "external-transfers.create";

    public async Task<IdempotentResult<ExternalTransferView>> HandleAsync(
        CreateExternalTransferCommand command, CancellationToken cancellationToken)
    {
        if (!IdempotencyRequest.IsValidKey(command.IdempotencyKey))
        {
            return Fail(Error.Validation("invalid_idempotency_key", "Idempotency-Key obrigatória: 1 a 64 caracteres [A-Za-z0-9_-]."));
        }

        if (!Currency.TryFromCode(command.Currency, out var currency))
        {
            return Fail(Error.Validation("unsupported_currency", $"Moeda não suportada: '{command.Currency}'."));
        }

        if (!Money.TryParse(command.Amount, currency, out var amount) || !amount.IsPositive)
        {
            return Fail(Error.Validation("invalid_amount", "Valor inválido. Use string decimal positiva, ex.: \"100.00\"."));
        }

        if (!ExternalDestination.TryCreate(command.DestinationBank, command.DestinationBranch, command.DestinationAccount, out var destination))
        {
            return Fail(Error.Validation("invalid_destination", "Destino inválido: banco (ISPB, 8 dígitos), agência (até 4 dígitos) e conta."));
        }

        var request = IdempotencyRequest.Create(
            command.Actor.Subject, Operation, command.IdempotencyKey!, command.SourceAccountId, amount.MinorUnits, currency.Code,
            destination.Bank, destination.Branch, destination.Account);

        return await executor.ExecuteAsync<ExternalTransferView>(request, async ct =>
        {
            var source = await access.FindOwnedAsync(command.Actor, command.SourceAccountId, ct);
            if (source is null)
            {
                return Error.NotFound("Conta de origem");
            }

            var balances = await ledger.LockBalancesAsync([source.LedgerAccountId], ct);
            await accounts.RefreshAsync(source, ct);

            var now = time.GetUtcNow();
            var today = BusinessCalendar.DateOf(now);
            var usedToday = await limitUsage.LockUsageAsync(LimitKind.TransferPerAccount, source.Id.ToString(), today, currency, ct);

            var (transfer, reservation) = ExternalTransfer.Create(
                source, balances[source.LedgerAccountId], amount, destination, command.Actor.Subject, command.IdempotencyKey!, usedToday, limits, now);

            if (reservation is not null)
            {
                ledger.Add(reservation);
                await limitUsage.AddUsageAsync(LimitKind.TransferPerAccount, source.Id.ToString(), today, amount, ct);
                outbox.Enqueue(new ExternalTransferCreatedV1(transfer.Id, source.Id, amount.ToDecimalString(), currency.Code), now);
            }

            transfers.Add(transfer);
            var view = ExternalTransferView.From(transfer);
            audit.Record(AuditEntry.Of(
                command.Actor,
                "external-transfer.create",
                "external_transfer",
                transfer.Id,
                view.RejectionReason is null ? view.Status : $"{view.Status}:{view.RejectionReason}",
                ("sourceAccountId", source.Id.ToString()),
                ("amount", view.Amount),
                ("destinationBank", transfer.DestinationBank)));
            if (view.RejectionReason is not null)
            {
                BankingTelemetry.RecordTransfer("external", "rejected", view.RejectionReason);
            }

            return view;
        }, cancellationToken);
    }

    private static IdempotentResult<ExternalTransferView> Fail(Error error) => new(error, Replayed: false);
}

public sealed class ExternalTransferQueriesHandler(IExternalTransferRepository transfers, AccountAccess access)
{
    public async Task<Result<ExternalTransferView>> GetAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        var transfer = await transfers.GetAsync(id, cancellationToken);
        if (transfer is null || await access.FindVisibleAsync(actor, transfer.SourceAccountId, cancellationToken) is null)
        {
            return Error.NotFound("Transferência externa");
        }

        return ExternalTransferView.From(transfer);
    }
}

public sealed class CancelExternalTransferHandler(
    IUnitOfWork unitOfWork,
    IExternalTransferRepository transfers,
    AccountAccess access,
    ILedger ledger,
    IOutbox outbox,
    IAuditTrail audit,
    TimeProvider time)
{
    public async Task<Result<ExternalTransferView>> HandleAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var transfer = await transfers.FindForUpdateAsync(id, cancellationToken);
        var source = transfer is null ? null : await access.FindOwnedAsync(actor, transfer.SourceAccountId, cancellationToken);
        if (transfer is null || source is null)
        {
            return Error.NotFound("Transferência externa");
        }

        var reservation = await ledger.FindTransactionByExternalIdAsync(transfer.ReservationExternalId, cancellationToken);
        if (reservation is null)
        {
            return Error.Conflict("not_cancellable", "Transferência sem reserva não pode ser cancelada.");
        }

        await ledger.LockBalancesAsync([source.LedgerAccountId], cancellationToken);
        var now = time.GetUtcNow();
        try
        {
            ledger.Add(transfer.Cancel(reservation, now));
        }
        catch (DomainException error)
        {
            return Error.Conflict(error.Code, error.Message);
        }

        outbox.Enqueue(new ExternalTransferCancelledV1(transfer.Id, transfer.SourceAccountId, transfer.Amount.ToDecimalString(), transfer.CurrencyCode), now);
        audit.Record(AuditEntry.Of(actor, "external-transfer.cancel", "external_transfer", transfer.Id, "cancelled"));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ExternalTransferView.From(transfer);
    }
}
