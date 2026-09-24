using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Banking.Application.Common;
using Banking.Application.Notifications;
using Banking.Contracts.Events;
using Banking.Infrastructure.Messaging;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Persistence.Configurations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Banking.Infrastructure.Notifications;

/// <summary>
/// Consumidor real dos eventos. O broker entrega pelo menos uma vez; a inbox, na mesma transação do efeito,
/// garante que cada evento é aplicado uma vez só.
/// </summary>
public sealed partial class NotificationsConsumer(
    IServiceScopeFactory scopes,
    RabbitMqConnectionProvider connections,
    IOptions<ConsumerOptions> options,
    TimeProvider time,
    ILogger<NotificationsConsumer> logger) : BackgroundService
{
    public const string ConsumerName = "notifications";
    public const string Queue = "banking.notifications";
    public const string DeadLetterQueue = "banking.notifications.dlq";

    /// <summary>Aplica o evento. Devolve false se ele já tinha sido processado.</summary>
    public async Task<bool> ProcessAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await using (var inbox = DbCommands.InTransaction(
            db,
            "INSERT INTO platform.inbox_messages (consumer, event_id, processed_at) VALUES (@consumer, @eventId, now()) ON CONFLICT DO NOTHING"))
        {
            inbox.Parameters.AddWithValue("consumer", ConsumerName);
            inbox.Parameters.AddWithValue("eventId", envelope.EventId);
            if (await inbox.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }
        }

        var now = time.GetUtcNow();
        foreach (var draft in NotificationProjector.Project(envelope))
        {
            db.Add(new NotificationRecord
            {
                Id = Guid.CreateVersion7(now),
                EventId = envelope.EventId,
                AccountId = draft.AccountId,
                Kind = draft.Kind,
                Amount = draft.Amount,
                Currency = draft.Currency,
                ReferenceId = draft.ReferenceId,
                CreatedAt = envelope.OccurredAt,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !connections.Options.IsConfigured)
        {
            LogDisabled();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilDisconnectedAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                LogConnectionLost(error);
            }

            await Task.Delay(options.Value.ReconnectDelay, time, stoppingToken);
        }
    }

    private async Task ConsumeUntilDisconnectedAsync(CancellationToken stoppingToken)
    {
        var connection = await connections.ConnectAsync("banking-notifications-consumer", stoppingToken);
        try
        {
            await ConsumeAsync(connection, stoppingToken);
        }
        finally
        {
            await RabbitMqConnectionProvider.AbortQuietlyAsync(connection);
        }
    }

    private async Task ConsumeAsync(IConnection connection, CancellationToken stoppingToken)
    {
        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(DeadLetterQueue, RabbitMqConnectionProvider.DeadLetterExchange, string.Empty, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(
            Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = RabbitMqConnectionProvider.DeadLetterExchange },
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(Queue, connections.Options.Exchange, "#", cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 20, global: false, stoppingToken);

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            disconnected.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(Queue, autoAck: false, consumer, stoppingToken);

        await disconnected.Task.WaitAsync(stoppingToken);
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        EventEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope>(delivery.Body.Span, Outbox.Json);
        }
        catch (JsonException error)
        {
            LogPoisonMessage(delivery.BasicProperties.MessageId, error);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        if (envelope is null)
        {
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        var traceParent = delivery.BasicProperties.Headers?.TryGetValue("traceparent", out var header) == true && header is byte[] bytes
            ? Encoding.UTF8.GetString(bytes)
            : null;
        ActivityContext.TryParse(traceParent, null, out var parent);
        using var activity = BankingTelemetry.Source.StartActivity($"consume {envelope.Type}.v{envelope.Version}", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.message.id", envelope.EventId);

        try
        {
            if (!await ProcessAsync(envelope, stoppingToken))
            {
                LogDuplicate(envelope.EventId);
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested)
        {
            LogProcessingFailed(envelope.EventId, error);
            await Task.Delay(TimeSpan.FromSeconds(1), time, stoppingToken);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumidor de notificações desligado (Consumers:Enabled ou Messaging:Uri)")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conexão do consumidor de notificações caiu; reconectando")]
    private partial void LogConnectionLost(Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mensagem {MessageId} ilegível, enviada para a dead-letter")]
    private partial void LogPoisonMessage(string? messageId, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Evento {EventId} já processado; ignorado pela inbox")]
    private partial void LogDuplicate(Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao processar {EventId}; devolvido à fila")]
    private partial void LogProcessingFailed(Guid eventId, Exception error);
}
