using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Banking.Infrastructure.Persistence;

/// <summary>SQL escrito à mão, sempre parametrizado e na conexão (e transação, se houver) do EF.</summary>
internal static class DbCommands
{
    public static NpgsqlCommand InTransaction(BankingDbContext db, string sql)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction
            ?? throw new InvalidOperationException("Este comando exige uma transação aberta.");
        return new NpgsqlCommand(sql, transaction.Connection, transaction);
    }

    public static async Task<T> ScalarAsync<T>(BankingDbContext db, string sql, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                sql,
                (NpgsqlConnection)db.Database.GetDbConnection(),
                db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
            return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
