using Banking.Application.Abstractions;
using Banking.Infrastructure.Persistence;

namespace Banking.Infrastructure.Platform;

internal sealed class InboxStore(BankingDbContext db) : IInbox
{
    public async Task<bool> TryRecordAsync(string consumer, Guid eventId, CancellationToken cancellationToken)
    {
        await using var command = DbCommands.InTransaction(
            db,
            "INSERT INTO platform.inbox_messages (consumer, event_id, processed_at) VALUES (@consumer, @eventId, now()) ON CONFLICT DO NOTHING");
        command.Parameters.AddWithValue("consumer", consumer);
        command.Parameters.AddWithValue("eventId", eventId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
