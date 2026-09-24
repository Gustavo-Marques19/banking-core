using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Banking.Application.Abstractions;
using Banking.Contracts.Events;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Persistence.Configurations;

namespace Banking.Infrastructure.Messaging;

internal sealed class Outbox(BankingDbContext db) : IOutbox
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Enqueue(object integrationEvent, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var type = integrationEvent.GetType().GetCustomAttribute<EventTypeAttribute>()
            ?? throw new InvalidOperationException($"{integrationEvent.GetType().Name} não tem [EventType].");

        var envelope = new EventEnvelope(
            Guid.CreateVersion7(occurredAt),
            type.Name,
            type.Version,
            occurredAt,
            JsonSerializer.SerializeToElement(integrationEvent, integrationEvent.GetType(), Json));

        db.Add(new OutboxMessageRecord
        {
            Id = envelope.EventId,
            Type = type.RoutingKey,
            Payload = JsonSerializer.Serialize(envelope, Json),
            OccurredAt = occurredAt,
            NextAttemptAt = occurredAt,
            TraceParent = Activity.Current?.Id,
        });
    }
}
