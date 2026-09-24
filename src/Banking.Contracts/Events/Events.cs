using System.Text.Json;

namespace Banking.Contracts.Events;

/// <summary>Nome e versão do evento. A routing key no broker é "{Name}.v{Version}".</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventTypeAttribute(string name, int version) : Attribute
{
    public string Name { get; } = name;

    public int Version { get; } = version;

    public string RoutingKey => $"{Name}.v{Version}";
}

/// <summary>Envelope publicado no broker. Consumidores deduplicam por <see cref="EventId"/>.</summary>
public sealed record EventEnvelope(Guid EventId, string Type, int Version, DateTimeOffset OccurredAt, JsonElement Data);

// Eventos não levam PII: só ids, valores e códigos (threat model T11).

[EventType("CustomerRegistered", 1)]
public sealed record CustomerRegisteredV1(Guid CustomerId);

[EventType("AccountOpened", 1)]
public sealed record AccountOpenedV1(Guid AccountId, Guid CustomerId, string Currency);

[EventType("AccountStatusChanged", 1)]
public sealed record AccountStatusChangedV1(Guid AccountId, string Status);

[EventType("MoneyDeposited", 1)]
public sealed record MoneyDepositedV1(Guid DepositId, Guid AccountId, string Amount, string Currency, Guid LedgerTransactionId);

[EventType("TransferCompleted", 1)]
public sealed record TransferCompletedV1(
    Guid TransferId, Guid SourceAccountId, Guid DestinationAccountId, string Amount, string Currency, Guid LedgerTransactionId);

[EventType("TransferRejected", 1)]
public sealed record TransferRejectedV1(Guid TransferId, Guid SourceAccountId, string Amount, string Currency, string Reason);

[EventType("ExternalTransferCreated", 1)]
public sealed record ExternalTransferCreatedV1(Guid ExternalTransferId, Guid SourceAccountId, string Amount, string Currency);

[EventType("ExternalTransferCompleted", 1)]
public sealed record ExternalTransferCompletedV1(Guid ExternalTransferId, Guid SourceAccountId, string Amount, string Currency);

[EventType("ExternalTransferFailed", 1)]
public sealed record ExternalTransferFailedV1(Guid ExternalTransferId, Guid SourceAccountId, string Amount, string Currency, string Reason);

[EventType("ExternalTransferCancelled", 1)]
public sealed record ExternalTransferCancelledV1(Guid ExternalTransferId, Guid SourceAccountId, string Amount, string Currency);

[EventType("ExternalTransferNeedsReview", 1)]
public sealed record ExternalTransferNeedsReviewV1(Guid ExternalTransferId, Guid SourceAccountId, int SubmitAttempts);
