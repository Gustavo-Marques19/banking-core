using Banking.Application.Abstractions;
using Banking.Application.Common;
using Banking.Domain.Accounts;
using Banking.Domain.Common;

namespace Banking.Application.Accounts;

public sealed record AccountView(
    Guid Id, Guid CustomerId, string Branch, string Number, string Currency, string Status, DateTimeOffset CreatedAt)
{
    public static AccountView From(Account account) => new(
        account.Id, account.CustomerId, account.Branch, account.Number, account.CurrencyCode, Codes.Of(account.Status), account.CreatedAt);
}

/// <summary>Na Fase 1 os dois saldos são iguais: a reserva de transferência externa é lançamento, não hold (ADR-006).</summary>
public sealed record BalanceView(Guid AccountId, string LedgerBalance, string AvailableBalance, string Currency, DateTimeOffset AsOf);

public sealed record StatementLineView(
    Guid TransactionId, string Type, string Description, string Direction, string Amount, string BalanceAfter, long Sequence, DateTimeOffset PostedAt);

public sealed record StatementView(Guid AccountId, string Currency, IReadOnlyList<StatementLineView> Lines, long? NextCursor);

/// <summary>Resolve se o ator pode ver a conta. Conta de outro cliente se comporta como inexistente (threat model T2).</summary>
public sealed class AccountAccess(IAccountRepository accounts, ICustomerRepository customers)
{
    public async Task<Account?> FindVisibleAsync(Actor actor, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(accountId, cancellationToken);
        if (account is null || actor.IsOperator)
        {
            return account;
        }

        var customer = await customers.FindBySubjectAsync(actor.Subject, cancellationToken);
        return customer?.Id == account.CustomerId ? account : null;
    }

    /// <summary>Só o dono movimenta. Nem operador debita conta de cliente por esta via.</summary>
    public async Task<Account?> FindOwnedAsync(Actor actor, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var customer = await customers.FindBySubjectAsync(actor.Subject, cancellationToken);
        return customer?.Id == account.CustomerId ? account : null;
    }
}

public sealed record OpenAccountCommand(Actor Actor, string? Currency);

public sealed class OpenAccountHandler(
    ICustomerRepository customers, IAccountRepository accounts, ILedger ledger, IUnitOfWork unitOfWork, TimeProvider time)
{
    public async Task<Result<AccountView>> HandleAsync(OpenAccountCommand command, CancellationToken cancellationToken)
    {
        if (!Currency.TryFromCode(command.Currency ?? Currency.Brl.Code, out var currency))
        {
            return Error.Validation("unsupported_currency", $"Moeda não suportada: '{command.Currency}'.");
        }

        var customer = await customers.FindBySubjectAsync(command.Actor.Subject, cancellationToken);
        if (customer is null)
        {
            return Error.Conflict("customer_not_registered", "Cadastre o cliente antes de abrir conta.");
        }

        var number = await accounts.NextNumberAsync(cancellationToken);
        var (account, ledgerAccount) = Account.Open(customer, number, currency, time.GetUtcNow());
        accounts.Add(account);
        ledger.Add(ledgerAccount);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AccountView.From(account);
    }
}

public sealed class AccountQueriesHandler(
    AccountAccess access, IAccountRepository accounts, ICustomerRepository customers, IAccountReadModel readModel, TimeProvider time)
{
    public const int MaxStatementPage = 100;

    public async Task<Result<AccountView>> GetAsync(Actor actor, Guid accountId, CancellationToken cancellationToken) =>
        await access.FindVisibleAsync(actor, accountId, cancellationToken) is { } account
            ? AccountView.From(account)
            : Error.NotFound("Conta");

    public async Task<IReadOnlyList<AccountView>> ListMineAsync(Actor actor, CancellationToken cancellationToken)
    {
        var customer = await customers.FindBySubjectAsync(actor.Subject, cancellationToken);
        return customer is null
            ? []
            : [.. (await accounts.ListByCustomerAsync(customer.Id, cancellationToken)).Select(AccountView.From)];
    }

    public async Task<Result<BalanceView>> GetBalanceAsync(Actor actor, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await access.FindVisibleAsync(actor, accountId, cancellationToken);
        if (account is null)
        {
            return Error.NotFound("Conta");
        }

        var balance = await readModel.GetBalanceAsync(account.LedgerAccountId, cancellationToken)
            ?? throw new InvalidOperationException($"Conta {account.Id} sem linha de saldo.");

        var text = balance.ToDecimalString();
        return new BalanceView(account.Id, text, text, account.CurrencyCode, time.GetUtcNow());
    }

    public async Task<Result<StatementView>> GetStatementAsync(
        Actor actor, Guid accountId, long? beforeSequence, int? limit, CancellationToken cancellationToken)
    {
        var pageSize = limit ?? 50;
        if (pageSize is < 1 or > MaxStatementPage)
        {
            return Error.Validation("invalid_limit", $"limit deve estar entre 1 e {MaxStatementPage}.");
        }

        var account = await access.FindVisibleAsync(actor, accountId, cancellationToken);
        if (account is null)
        {
            return Error.NotFound("Conta");
        }

        var lines = await readModel.GetStatementAsync(account.LedgerAccountId, beforeSequence, pageSize, cancellationToken);
        long? next = lines.Count == pageSize ? lines[^1].Sequence : null;

        return new StatementView(
            account.Id,
            account.CurrencyCode,
            [.. lines.Select(l => new StatementLineView(
                l.TransactionId, l.Type, l.Description, l.Direction, l.Amount.ToDecimalString(), l.BalanceAfter.ToDecimalString(), l.Sequence, l.PostedAt))],
            next);
    }
}

public sealed class AccountStatusHandler(IAccountRepository accounts, ILedger ledger, IUnitOfWork unitOfWork)
{
    public Task<Result<AccountView>> BlockAsync(Actor actor, Guid accountId, CancellationToken cancellationToken) =>
        ChangeAsync(actor, accountId, a => a.Block(), cancellationToken);

    public Task<Result<AccountView>> UnblockAsync(Actor actor, Guid accountId, CancellationToken cancellationToken) =>
        ChangeAsync(actor, accountId, a => a.Unblock(), cancellationToken);

    private async Task<Result<AccountView>> ChangeAsync(
        Actor actor, Guid accountId, Action<Account> change, CancellationToken cancellationToken)
    {
        if (!actor.IsOperator)
        {
            return Error.Forbidden("operator_required", "Só operador altera o status de conta.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var account = await accounts.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Error.NotFound("Conta");
        }

        // Mesmo lock das operações financeiras: nenhuma transferência decide com o status antigo.
        await ledger.LockBalancesAsync([account.LedgerAccountId], cancellationToken);
        await accounts.RefreshAsync(account, cancellationToken);

        try
        {
            change(account);
        }
        catch (DomainException error)
        {
            return Error.Conflict(error.Code, error.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountView.From(account);
    }
}
