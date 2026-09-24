using System.Data;
using System.Globalization;
using Banking.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(BankingDbContext db, IOptions<DatabaseOptions> options) : IUnitOfWork
{
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var lockTimeout = Milliseconds(options.Value.LockTimeout);
        var statementTimeout = Milliseconds(options.Value.StatementTimeout);
        await db.Database.ExecuteSqlAsync(
            $"SELECT set_config('lock_timeout', {lockTimeout}, true), set_config('statement_timeout', {statementTimeout}, true)",
            cancellationToken);

        return new EfUnitOfWorkTransaction(transaction);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private static string Milliseconds(TimeSpan value) =>
        ((long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + "ms";

    private sealed class EfUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
