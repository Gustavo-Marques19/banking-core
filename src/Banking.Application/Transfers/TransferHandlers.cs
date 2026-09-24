using Banking.Application.Abstractions;
using Banking.Application.Accounts;
using Banking.Application.Common;
using Banking.Application.Idempotency;
using Banking.Domain.Common;
using Banking.Domain.Payments;

namespace Banking.Application.Transfers;

public sealed record TransferView(
    Guid Id,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    string Amount,
    string Currency,
    string? Description,
    string Status,
    string? RejectionReason,
    Guid? LedgerTransactionId,
    DateTimeOffset CreatedAt)
{
    public static TransferView From(InternalTransfer transfer) => new(
        transfer.Id,
        transfer.SourceAccountId,
        transfer.DestinationAccountId,
        transfer.Amount.ToDecimalString(),
        transfer.CurrencyCode,
        transfer.Description,
        Codes.Of(transfer.Status),
        Codes.Of(transfer.RejectionReason),
        transfer.LedgerTransactionId,
        transfer.CreatedAt);
}

public sealed record CreateTransferCommand(
    Actor Actor,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    string? Amount,
    string? Currency,
    string? Description,
    string? IdempotencyKey);

public sealed class CreateTransferHandler(
    IdempotentExecutor executor,
    AccountAccess access,
    IAccountRepository accounts,
    ITransferRepository transfers,
    ILedger ledger,
    ILimitUsageStore limitUsage,
    TransferLimits limits,
    TimeProvider time)
{
    public const string Operation = "transfers.create";

    public async Task<IdempotentResult<TransferView>> HandleAsync(CreateTransferCommand command, CancellationToken cancellationToken)
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

        if (command.SourceAccountId == command.DestinationAccountId)
        {
            return Fail(Error.Validation("same_account", "Origem e destino são a mesma conta."));
        }

        var request = IdempotencyRequest.Create(
            command.Actor.Subject,
            Operation,
            command.IdempotencyKey!,
            command.SourceAccountId,
            command.DestinationAccountId,
            amount.MinorUnits,
            currency.Code,
            command.Description?.Trim() ?? string.Empty);

        return await executor.ExecuteAsync<TransferView>(request, async ct =>
        {
            // Conta de outro cliente e conta inexistente dão a mesma resposta (threat model T2).
            var source = await access.FindOwnedAsync(command.Actor, command.SourceAccountId, ct);
            if (source is null)
            {
                return Error.NotFound("Conta de origem");
            }

            var destination = await accounts.FindAsync(command.DestinationAccountId, ct);
            if (destination is null)
            {
                return Error.NotFound("Conta de destino");
            }

            var balances = await ledger.LockBalancesAsync([source.LedgerAccountId, destination.LedgerAccountId], ct);
            await accounts.RefreshAsync(source, ct);
            await accounts.RefreshAsync(destination, ct);

            var now = time.GetUtcNow();
            var today = BusinessCalendar.DateOf(now);
            var usedToday = await limitUsage.LockUsageAsync(LimitKind.TransferPerAccount, source.Id.ToString(), today, currency, ct);

            InternalTransfer transfer;
            try
            {
                (transfer, var posting) = InternalTransfer.Decide(
                    source,
                    destination,
                    balances[source.LedgerAccountId],
                    amount,
                    command.Description,
                    command.Actor.Subject,
                    command.IdempotencyKey!,
                    usedToday,
                    limits,
                    now);

                if (posting is not null)
                {
                    ledger.Add(posting);
                    await limitUsage.AddUsageAsync(LimitKind.TransferPerAccount, source.Id.ToString(), today, amount, ct);
                }
            }
            catch (DomainException error)
            {
                return Error.Validation(error.Code, error.Message);
            }

            transfers.Add(transfer);
            return TransferView.From(transfer);
        }, cancellationToken);
    }

    private static IdempotentResult<TransferView> Fail(Error error) => new(error, Replayed: false);
}

public sealed class GetTransferHandler(ITransferRepository transfers, AccountAccess access)
{
    public async Task<Result<TransferView>> HandleAsync(Actor actor, Guid transferId, CancellationToken cancellationToken)
    {
        var transfer = await transfers.FindAsync(transferId, cancellationToken);
        if (transfer is null)
        {
            return Error.NotFound("Transferência");
        }

        var visible = actor.IsOperator
            || await access.FindVisibleAsync(actor, transfer.SourceAccountId, cancellationToken) is not null
            || await access.FindVisibleAsync(actor, transfer.DestinationAccountId, cancellationToken) is not null;

        return visible ? TransferView.From(transfer) : Error.NotFound("Transferência");
    }
}
