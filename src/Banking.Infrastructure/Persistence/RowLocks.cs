using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Persistence;

internal static class RowLocks
{
    /// <summary>
    /// Trava a linha e devolve a entidade com o estado do banco. Se ela já estava rastreada, é recarregada:
    /// uma leitura anterior ao lock pode estar velha.
    /// </summary>
    public static Task<T?> FindForUpdateAsync<T>(BankingDbContext db, string table, Guid id, Func<T, Guid> key, CancellationToken cancellationToken)
        where T : class =>
        PostgresErrors.TranslateAsync(async () =>
        {
            await using var command = DbCommands.InTransaction(db, $"SELECT 1 FROM {table} WHERE id = @id FOR UPDATE");
            command.Parameters.AddWithValue("id", id);
            if (await command.ExecuteScalarAsync(cancellationToken) is null)
            {
                return null;
            }

            var tracked = db.ChangeTracker.Entries<T>().FirstOrDefault(e => key(e.Entity) == id);
            if (tracked is not null)
            {
                await tracked.ReloadAsync(cancellationToken);
                return tracked.Entity;
            }

            return await db.Set<T>().FindAsync([id], cancellationToken);
        });
}
