using Banking.Application.Abstractions;
using Banking.Application.Common;

namespace Banking.Application.ExternalTransfers;

public enum WebhookOutcome
{
    Scheduled,
    Duplicate,
    UnknownTransfer,
}

/// <summary>
/// Webhook do provider. Não confia no conteúdo: só antecipa a próxima consulta de status, e é ela que move dinheiro.
/// </summary>
public sealed class ProviderWebhookHandler(
    IUnitOfWork unitOfWork, IInbox inbox, IExternalTransferRepository transfers, TimeProvider time)
{
    public const string Consumer = "provider-webhook";

    public async Task<WebhookOutcome> HandleAsync(Guid eventId, string clientReference, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(clientReference, "N", out var transferId))
        {
            return WebhookOutcome.UnknownTransfer;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        if (!await inbox.TryRecordAsync(Consumer, eventId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return WebhookOutcome.Duplicate;
        }

        var transfer = await transfers.FindForUpdateAsync(transferId, cancellationToken);
        if (transfer is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return WebhookOutcome.UnknownTransfer;
        }

        transfer.ScheduleCheck(time.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WebhookOutcome.Scheduled;
    }
}
