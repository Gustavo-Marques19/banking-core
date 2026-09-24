using Banking.Application.Abstractions;
using Banking.Application.Audit;
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
    string? DecidedBy,
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
        deposit.DecidedBy,
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
    IAuditTrail audit,
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
            audit.Record(AuditEntry.Of(
                command.Actor,
                "deposit.create",
                "deposit",
                deposit.Id,
                Outcome(deposit.Status, deposit.RejectionReason),
                ("accountId", account.Id.ToString()),
                ("amount", amount.ToDecimalString()),
                ("currency", amount.Currency.Code)));
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

    private static string Outcome(DepositStatus status, RejectionReason? reason) =>
        reason is null ? Codes.Of(status) : $"{Codes.Of(status)}:{Codes.Of(reason)}";
}

/// <summary>Maker-checker do depósito acima do limite de aprovação: outro operador decide (threat model T4).</summary>
public sealed class DepositApprovalHandler(
    IUnitOfWork unitOfWork,
    IDepositRepository deposits,
    IAccountRepository accounts,
    ILedger ledger,
    ILimitUsageStore limitUsage,
    IOutbox outbox,
    IAuditTrail audit,
    DepositLimits limits,
    TimeProvider time)
{
    public Task<Result<DepositView>> ApproveAsync(Actor actor, Guid depositId, CancellationToken cancellationToken) =>
        DecideAsync(actor, depositId, approve: true, cancellationToken);

    public Task<Result<DepositView>> RejectAsync(Actor actor, Guid depositId, CancellationToken cancellationToken) =>
        DecideAsync(actor, depositId, approve: false, cancellationToken);

    private async Task<Result<DepositView>> DecideAsync(Actor actor, Guid depositId, bool approve, CancellationToken cancellationToken)
    {
        if (!actor.IsOperator)
        {
            return Error.NotFound("Depósito");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var deposit = await deposits.FindForUpdateAsync(depositId, cancellationToken);
        if (deposit is null)
        {
            return Error.NotFound("Depósito");
        }

        var account = await accounts.FindAsync(deposit.AccountId, cancellationToken)
            ?? throw new InvalidOperationException($"Conta {deposit.AccountId} não encontrada.");
        await ledger.LockBalancesAsync([account.LedgerAccountId], cancellationToken);
        await accounts.RefreshAsync(account, cancellationToken);

        var now = time.GetUtcNow();
        var today = BusinessCalendar.DateOf(now);
        try
        {
            if (approve)
            {
                var usedToday = await limitUsage.LockUsageAsync(LimitKind.DepositPerOperator, deposit.RequestedBy, today, deposit.Amount.Currency, cancellationToken);
                var posting = deposit.Approve(actor.Subject, account, usedToday, limits, now);
                if (posting is not null)
                {
                    ledger.Add(posting);
                    await limitUsage.AddUsageAsync(LimitKind.DepositPerOperator, deposit.RequestedBy, today, deposit.Amount, cancellationToken);
                    outbox.Enqueue(
                        new MoneyDepositedV1(deposit.Id, account.Id, deposit.Amount.ToDecimalString(), deposit.CurrencyCode, posting.Id), now);
                }
            }
            else
            {
                deposit.Reject(actor.Subject, now);
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
            approve ? "deposit.approve" : "deposit.reject",
            "deposit",
            deposit.Id,
            deposit.RejectionReason is { } reason ? $"{Codes.Of(deposit.Status)}:{Codes.Of(reason)}" : Codes.Of(deposit.Status),
            ("requestedBy", deposit.RequestedBy)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return DepositView.From(deposit);
    }
}
