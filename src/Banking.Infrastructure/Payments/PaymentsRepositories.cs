using Banking.Application.Abstractions;
using Banking.Domain.Common;
using Banking.Domain.Payments;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Payments;

internal sealed class DepositRepository(BankingDbContext db) : IDepositRepository
{
    public void Add(Deposit deposit) => db.Add(deposit);

    public Task<Deposit?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Deposit>().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
}

internal sealed class TransferRepository(BankingDbContext db) : ITransferRepository
{
    public void Add(InternalTransfer transfer) => db.Add(transfer);

    public Task<InternalTransfer?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<InternalTransfer>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
}

internal sealed class LimitUsageStore(BankingDbContext db) : ILimitUsageStore
{
    public Task<Money> LockUsageAsync(
        LimitKind kind, string subject, DateOnly date, Currency currency, CancellationToken cancellationToken) =>
        PostgresErrors.TranslateAsync(async () =>
        {
            await using (var insert = DbCommands.InTransaction(
                db,
                """
                INSERT INTO payments.limit_usage (limit_kind, subject, usage_date, used_minor)
                VALUES (@kind, @subject, @date, 0)
                ON CONFLICT DO NOTHING
                """))
            {
                AddKey(insert, kind, subject, date);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var select = DbCommands.InTransaction(
                db,
                """
                SELECT used_minor FROM payments.limit_usage
                 WHERE limit_kind = @kind AND subject = @subject AND usage_date = @date
                   FOR UPDATE
                """);
            AddKey(select, kind, subject, date);
            var used = (long)(await select.ExecuteScalarAsync(cancellationToken))!;
            return Money.FromMinor(used, currency);
        });

    public Task AddUsageAsync(LimitKind kind, string subject, DateOnly date, Money amount, CancellationToken cancellationToken) =>
        PostgresErrors.TranslateAsync(async () =>
        {
            await using var command = DbCommands.InTransaction(
                db,
                """
                UPDATE payments.limit_usage SET used_minor = used_minor + @amount
                 WHERE limit_kind = @kind AND subject = @subject AND usage_date = @date
                """);
            AddKey(command, kind, subject, date);
            command.Parameters.AddWithValue("amount", amount.MinorUnits);
            await command.ExecuteNonQueryAsync(cancellationToken);
        });

    private static void AddKey(Npgsql.NpgsqlCommand command, LimitKind kind, string subject, DateOnly date)
    {
        command.Parameters.AddWithValue("kind", kind.ToString());
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("date", date);
    }
}
