using Banking.Application.Abstractions;
using Banking.Domain.Common;
using Banking.Domain.Ledger;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Banking.Infrastructure.Ledger;

internal sealed class LedgerRepository(BankingDbContext db) : ILedger
{
    public async Task<IReadOnlyDictionary<Guid, LedgerBalance>> LockBalancesAsync(
        IReadOnlyCollection<Guid> ledgerAccountIds, CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction
            ?? throw new InvalidOperationException("Lock de saldo exige uma transação aberta.");

        // A função é SECURITY DEFINER: a role da aplicação trava as linhas sem ter UPDATE na tabela (ADR-008).
        await using var command = new NpgsqlCommand(
            "SELECT ledger_account_id, currency, normal_balance, balance_minor, allow_negative FROM ledger.lock_balances(@ids)",
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("ids", ledgerAccountIds.Distinct().ToArray());

        var balances = new Dictionary<Guid, LedgerBalance>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            var currency = Currency.FromCode(reader.GetString(1));
            balances[id] = new LedgerBalance(
                id,
                Money.FromMinor(reader.GetInt64(3), currency),
                reader.GetString(2) == "D" ? EntryDirection.Debit : EntryDirection.Credit,
                reader.GetBoolean(4));
        }

        return balances;
    }

    public void Add(LedgerAccount account) => db.Add(account);

    public void Add(LedgerTransaction transaction) => db.Add(transaction);

    public Task<LedgerTransaction?> FindTransactionAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<LedgerTransaction>().AsNoTracking().Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<LedgerTransaction?> FindTransactionByExternalIdAsync(string externalId, CancellationToken cancellationToken) =>
        db.Set<LedgerTransaction>().AsNoTracking().Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.ExternalId == externalId, cancellationToken);
}
