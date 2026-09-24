using Banking.Application.Abstractions;
using Banking.Application.Common;
using Banking.Application.Idempotency;
using Banking.Contracts.Events;
using Banking.Domain.Common;
using Banking.Domain.Payments;

namespace Banking.Application.Deposits;

public sealed record DepositView(
    Guid Id,
    Guid AccountId,
    string Amount,
    string Currency,
    string Reason,
    string Status,
    string? RejectionReason,
    string RequestedBy,
    DateTimeOffset CreatedAt)
{
    public static DepositView From(Deposit deposit) => new(
        deposit.Id,
        deposit.AccountId,
        deposit.Amount.ToDecimalString(),
        deposit.CurrencyCode,
        deposit.Reason,
        Codes.Of(deposit.Status),
        Codes.Of(deposit.RejectionReason),
        deposit.RequestedBy,
        deposit.CreatedAt);
}

public sealed record MakeDepositCommand(
    Actor Actor, Guid AccountId, string? Amount, string? Currency, string? Reason, string? IdempotencyKey);

public sealed class MakeDepositHandler(
    IdempotentExecutor executor,
    IAccountRepository accounts,
    IDepositRepository deposits,
    ILedger ledger,
    ILimitUsageStore limitUsage,
    IOutbox outbox,
    DepositLimits limits,
    TimeProvider time)
{
    public const string Operation = "deposits.create";

    public async Task<IdempotentResult<DepositView>> HandleAsync(MakeDepositCommand command, CancellationToken cancellationToken)
    {
        if (!command.Actor.IsOperator)
        {
            return Fail(Error.Forbidden("operator_required", "Só operador faz depósito."));
        }

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

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Fail(Error.Validation("invalid_reason", "Motivo obrigatório."));
        }

        var request = IdempotencyRequest.Create(
            command.Actor.Subject, Operation, command.IdempotencyKey!, command.AccountId, amount.MinorUnits, currency.Code, command.Reason.Trim());

        return await executor.ExecuteAsync<DepositView>(request, async ct =>
        {
            var account = await accounts.FindAsync(command.AccountId, ct);
            if (account is null)
            {
                return Error.NotFound("Conta");
            }

            await ledger.LockBalancesAsync([account.LedgerAccountId], ct);
            await accounts.RefreshAsync(account, ct);

            var now = time.GetUtcNow();
            var today = BusinessCalendar.DateOf(now);
            var usedToday = await limitUsage.LockUsageAsync(LimitKind.DepositPerOperator, command.Actor.Subject, today, currency, ct);

            Deposit deposit;
            try
            {
                (deposit, var posting) = Deposit.Decide(
                    account, amount, command.Reason!, command.Actor.Subject, command.IdempotencyKey!, usedToday, limits, now);

                if (posting is not null)
                {
                    ledger.Add(posting);
                    await limitUsage.AddUsageAsync(LimitKind.DepositPerOperator, command.Actor.Subject, today, amount, ct);
                    outbox.Enqueue(
                        new MoneyDepositedV1(deposit.Id, account.Id, amount.ToDecimalString(), amount.Currency.Code, posting.Id), now);
                }
            }
            catch (DomainException error)
            {
                return Error.Validation(error.Code, error.Message);
            }

            deposits.Add(deposit);
            return DepositView.From(deposit);
        }, cancellationToken);
    }

    public async Task<Result<DepositView>> GetAsync(Actor actor, Guid depositId, CancellationToken cancellationToken)
    {
        if (!actor.IsOperator)
        {
            return Error.NotFound("Depósito");
        }

        return await deposits.FindAsync(depositId, cancellationToken) is { } deposit
            ? DepositView.From(deposit)
            : Error.NotFound("Depósito");
    }

    private static IdempotentResult<DepositView> Fail(Error error) => new(error, Replayed: false);
}
