using Banking.Application.Abstractions;
using Banking.Application.Audit;
using Banking.Application.Common;
using Banking.Contracts.Events;
using Banking.Domain.Accounts;
using Banking.Domain.Common;

namespace Banking.Application.Accounts;

public sealed record AccountView(
    Guid Id, Guid CustomerId, string Branch, string Number, string Currency, string Status, DateTimeOffset CreatedAt)
{
    public static AccountView From(Account account) => new(
        account.Id, account.CustomerId, account.Branch, account.Number, account.CurrencyCode, Codes.Of(account.Status), account.CreatedAt);
}

/// <summary>
/// O que quem vai transferir precisa para conferir o destino: o id para a transferência e o titular mascarado
/// (primeiro nome e inicial do sobrenome). Nada de CPF nem status.
/// </summary>
public sealed record AccountLookupView(Guid Id, string Branch, string Number, string Currency, string HolderName);

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
    ICustomerRepository customers,
    IAccountRepository accounts,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    IAuditTrail audit,
    TimeProvider time)
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
        outbox.Enqueue(new AccountOpenedV1(account.Id, account.CustomerId, account.CurrencyCode), account.CreatedAt);
        audit.Record(AuditEntry.Of(command.Actor, "account.open", "account", account.Id, "completed", ("number", account.Number)));
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

/// <summary>
/// Busca de conta de destino por agência e número. O nome vem mascarado e o endpoint tem limite próprio, porque varrer
/// números é o caminho óbvio para montar uma lista de clientes (threat model T22).
/// </summary>
public sealed class AccountLookupHandler(IAccountRepository accounts, ICustomerRepository customers)
{
    private const int NumberLength = 8;

    public async Task<Result<AccountLookupView>> HandleAsync(string? branch, string? number, CancellationToken cancellationToken)
    {
        var digits = number?.Trim() ?? string.Empty;
        if (branch?.Trim() is not { Length: 4 } normalizedBranch || !normalizedBranch.All(char.IsAsciiDigit)
            || digits.Length is 0 or > NumberLength || !digits.All(char.IsAsciiDigit))
        {
            return Error.Validation("invalid_account_number", "Informe a agência com 4 dígitos e o número da conta com até 8.");
        }

        var account = await accounts.FindByNumberAsync(normalizedBranch, digits.PadLeft(NumberLength, '0'), cancellationToken);
        var holder = account is null ? null : await customers.FindAsync(account.CustomerId, cancellationToken);
        return account is null || holder is null
            ? Error.NotFound("Conta")
            : new AccountLookupView(account.Id, account.Branch, account.Number, account.CurrencyCode, MaskName(holder.Name));
    }

    public static string MaskName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0] : $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.";
    }
}

public sealed class AccountStatusHandler(
    IAccountRepository accounts, ILedger ledger, IUnitOfWork unitOfWork, IOutbox outbox, IAuditTrail audit, TimeProvider time)
{
    public Task<Result<AccountView>> BlockAsync(Actor actor, Guid accountId, CancellationToken cancellationToken) =>
        ChangeAsync(actor, accountId, "account.block", a => a.Block(), cancellationToken);

    public Task<Result<AccountView>> UnblockAsync(Actor actor, Guid accountId, CancellationToken cancellationToken) =>
        ChangeAsync(actor, accountId, "account.unblock", a => a.Unblock(), cancellationToken);

    private async Task<Result<AccountView>> ChangeAsync(
        Actor actor, Guid accountId, string operation, Action<Account> change, CancellationToken cancellationToken)
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

        outbox.Enqueue(new AccountStatusChangedV1(account.Id, Codes.Of(account.Status)), time.GetUtcNow());
        audit.Record(AuditEntry.Of(actor, operation, "account", account.Id, Codes.Of(account.Status)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountView.From(account);
    }
}
