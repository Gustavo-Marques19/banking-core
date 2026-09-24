using Banking.Application.Abstractions;
using Banking.Domain.Payments;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Payments;

internal sealed class ExternalTransferRepository(BankingDbContext db) : IExternalTransferRepository
{
    public void Add(ExternalTransfer transfer) => db.Add(transfer);

    public Task<ExternalTransfer?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ExternalTransfer>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<ExternalTransfer?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        PostgresErrors.TranslateAsync(async () =>
        {
            await using var command = DbCommands.InTransaction(db, "SELECT 1 FROM payments.external_transfers WHERE id = @id FOR UPDATE");
            command.Parameters.AddWithValue("id", id);
            if (await command.ExecuteScalarAsync(cancellationToken) is null)
            {
                return null;
            }

            return await LoadCurrentAsync(id, cancellationToken);
        });

    public Task<IReadOnlyList<ExternalTransfer>> LockCreatedAsync(int limit, CancellationToken cancellationToken) =>
        PostgresErrors.TranslateAsync<IReadOnlyList<ExternalTransfer>>(async () =>
        {
            await using var command = DbCommands.InTransaction(
                db,
                """
                SELECT id FROM payments.external_transfers
                 WHERE status = 'Created'
                 ORDER BY created_at
                 LIMIT @limit
                   FOR UPDATE SKIP LOCKED
                """);
            command.Parameters.AddWithValue("limit", limit);

            var ids = new List<Guid>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    ids.Add(reader.GetGuid(0));
                }
            }

            var transfers = new List<ExternalTransfer>(ids.Count);
            foreach (var id in ids)
            {
                transfers.Add((await LoadCurrentAsync(id, cancellationToken))!);
            }

            return transfers;
        });

    public async Task<IReadOnlyList<Guid>> ListDueForCheckAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await db.Set<ExternalTransfer>().AsNoTracking()
            .Where(t => (t.Status == ExternalTransferStatus.Unknown || t.Status == ExternalTransferStatus.Processing)
                && !t.RequiresManualReview
                && t.NextCheckAt <= now)
            .OrderBy(t => t.NextCheckAt)
            .Take(limit)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Com a linha já travada, garante que a entidade rastreada reflete o banco, e não uma leitura antiga.</summary>
    private async Task<ExternalTransfer?> LoadCurrentAsync(Guid id, CancellationToken cancellationToken)
    {
        var tracked = db.ChangeTracker.Entries<ExternalTransfer>().FirstOrDefault(e => e.Entity.Id == id);
        if (tracked is not null)
        {
            await tracked.ReloadAsync(cancellationToken);
            return tracked.Entity;
        }

        return await db.Set<ExternalTransfer>().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }
}
