using Banking.Application.Notifications;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Notifications;

internal sealed class NotificationReadModel(BankingDbContext db) : INotificationReadModel
{
    public async Task<IReadOnlyList<NotificationView>> ListAsync(Guid accountId, int limit, CancellationToken cancellationToken) =>
        await db.Set<NotificationRecord>().AsNoTracking()
            .Where(n => n.AccountId == accountId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new NotificationView(n.Id, n.Kind, n.Amount, n.Currency, n.ReferenceId, n.CreatedAt))
            .ToListAsync(cancellationToken);
}
