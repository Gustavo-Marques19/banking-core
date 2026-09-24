using Banking.Application.Abstractions;
using Banking.Domain.Accounts;
using Banking.Domain.Common;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Accounts;

internal sealed class CustomerRepository(BankingDbContext db) : ICustomerRepository
{
    public void Add(Customer customer) => db.Add(customer);

    public Task<Customer?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Customer>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Customer?> FindBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        db.Set<Customer>().FirstOrDefaultAsync(c => c.Subject == subject, cancellationToken);

    public Task<bool> ExistsWithDocumentAsync(byte[] blindIndex, CancellationToken cancellationToken) =>
        db.Set<Customer>().AnyAsync(c => c.DocumentBlindIndex == blindIndex, cancellationToken);
}

internal sealed class AccountRepository(BankingDbContext db) : IAccountRepository
{
    public void Add(Account account) => db.Add(account);

    public Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Account>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task RefreshAsync(Account account, CancellationToken cancellationToken) =>
        db.Entry(account).ReloadAsync(cancellationToken);

    public async Task<IReadOnlyList<Account>> ListByCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.Set<Account>().AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Account?> FindByNumberAsync(string branch, string number, CancellationToken cancellationToken) =>
        db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Branch == branch && a.Number == number, cancellationToken);

    public Task<long> NextNumberAsync(CancellationToken cancellationToken) =>
        DbCommands.ScalarAsync<long>(db, "SELECT nextval('accounts.account_number_seq')", cancellationToken);
}

internal sealed class AccountReadModel(BankingDbContext db) : IAccountReadModel
{
    public async Task<Money?> GetBalanceAsync(Guid ledgerAccountId, CancellationToken cancellationToken)
    {
        var row = await db.Set<AccountBalanceRecord>().AsNoTracking()
            .FirstOrDefaultAsync(b => b.LedgerAccountId == ledgerAccountId, cancellationToken);
        return row is null ? null : Money.FromMinor(row.BalanceMinor, Currency.FromCode(row.Currency));
    }

    public async Task<IReadOnlyList<StatementLine>> GetStatementAsync(
        Guid ledgerAccountId, long? beforeSequence, int limit, CancellationToken cancellationToken)
    {
        var query =
            from entry in db.Set<Domain.Ledger.LedgerEntry>().AsNoTracking()
            join transaction in db.Set<Domain.Ledger.LedgerTransaction>().AsNoTracking() on entry.TransactionId equals transaction.Id
            where entry.LedgerAccountId == ledgerAccountId
                && (beforeSequence == null || entry.AccountSequence < beforeSequence)
            orderby entry.AccountSequence descending
            select new
            {
                transaction.Id,
                transaction.ExternalId,
                transaction.Type,
                transaction.Description,
                entry.Direction,
                entry.AmountMinor,
                entry.CurrencyCode,
                entry.BalanceAfterMinor,
                entry.AccountSequence,
                transaction.PostedAt,
            };

        var rows = await query.Take(limit).ToListAsync(cancellationToken);
        return [.. rows.Select(r =>
        {
            var currency = Currency.FromCode(r.CurrencyCode);
            return new StatementLine(
                r.Id,
                Domain.Ledger.LedgerTransaction.OperationIdOf(r.ExternalId),
                Application.Common.Codes.Of(r.Type),
                r.Description,
                r.Direction == Domain.Ledger.EntryDirection.Debit ? "debit" : "credit",
                Money.FromMinor(r.AmountMinor, currency),
                Money.FromMinor(r.BalanceAfterMinor ?? 0, currency),
                r.AccountSequence ?? 0,
                r.PostedAt);
        })];
    }
}
