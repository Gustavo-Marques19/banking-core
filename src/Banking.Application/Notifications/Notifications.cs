using System.Text.Json;
using Banking.Application.Accounts;
using Banking.Application.Common;
using Banking.Contracts.Events;

namespace Banking.Application.Notifications;

public sealed record NotificationDraft(Guid AccountId, string Kind, string? Amount, string? Currency, Guid ReferenceId);

public sealed record NotificationView(Guid Id, string Kind, string? Amount, string? Currency, Guid ReferenceId, DateTimeOffset CreatedAt);

/// <summary>Traduz eventos em notificações por conta. Evento desconhecido não gera nada.</summary>
public static class NotificationProjector
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<NotificationDraft> Project(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return $"{envelope.Type}.v{envelope.Version}" switch
        {
            "MoneyDeposited.v1" when Read<MoneyDepositedV1>(envelope) is { } e =>
                [new(e.AccountId, "deposit_received", e.Amount, e.Currency, e.DepositId)],
            "TransferCompleted.v1" when Read<TransferCompletedV1>(envelope) is { } e =>
            [
                new(e.SourceAccountId, "transfer_sent", e.Amount, e.Currency, e.TransferId),
                new(e.DestinationAccountId, "transfer_received", e.Amount, e.Currency, e.TransferId),
            ],
            "TransferRejected.v1" when Read<TransferRejectedV1>(envelope) is { } e =>
                [new(e.SourceAccountId, $"transfer_rejected:{e.Reason}", e.Amount, e.Currency, e.TransferId)],
            "ExternalTransferCompleted.v1" when Read<ExternalTransferCompletedV1>(envelope) is { } e =>
                [new(e.SourceAccountId, "external_transfer_completed", e.Amount, e.Currency, e.ExternalTransferId)],
            "ExternalTransferFailed.v1" when Read<ExternalTransferFailedV1>(envelope) is { } e =>
                [new(e.SourceAccountId, "external_transfer_failed", e.Amount, e.Currency, e.ExternalTransferId)],
            "ExternalTransferCancelled.v1" when Read<ExternalTransferCancelledV1>(envelope) is { } e =>
                [new(e.SourceAccountId, "external_transfer_cancelled", e.Amount, e.Currency, e.ExternalTransferId)],
            "TransferReversed.v1" when Read<TransferReversedV1>(envelope) is { } e =>
            [
                new(e.SourceAccountId, "transfer_reversal_received", e.Amount, e.Currency, e.TransferId),
                new(e.DestinationAccountId, "transfer_reversal_debited", e.Amount, e.Currency, e.TransferId),
            ],
            "AccountStatusChanged.v1" when Read<AccountStatusChangedV1>(envelope) is { } e =>
                [new(e.AccountId, $"account_{e.Status}", null, null, e.AccountId)],
            _ => [],
        };
    }

    private static T? Read<T>(EventEnvelope envelope) => envelope.Data.Deserialize<T>(Json);
}

public interface INotificationReadModel
{
    Task<IReadOnlyList<NotificationView>> ListAsync(Guid accountId, int limit, CancellationToken cancellationToken);
}

public sealed class NotificationQueriesHandler(AccountAccess access, INotificationReadModel readModel)
{
    public async Task<Result<IReadOnlyList<NotificationView>>> ListAsync(Actor actor, Guid accountId, CancellationToken cancellationToken)
    {
        if (await access.FindVisibleAsync(actor, accountId, cancellationToken) is null)
        {
            return Error.NotFound("Conta");
        }

        return Result<IReadOnlyList<NotificationView>>.From(await readModel.ListAsync(accountId, 50, cancellationToken));
    }
}
