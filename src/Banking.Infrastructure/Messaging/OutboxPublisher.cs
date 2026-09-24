using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Messaging;

/// <summary>
/// Publica mensagens pendentes do outbox. Entrega pelo menos uma vez: o consumidor deduplica pelo event id (inbox).
/// Várias instâncias podem rodar juntas; SKIP LOCKED divide o trabalho.
/// </summary>
public sealed partial class OutboxPublisher(
    IServiceScopeFactory scopes,
    IMessagePublisher publisher,
    IOptions<OutboxOptions> options,
    IOptions<MessagingOptions> messaging,
    TimeProvider time,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private sealed record PendingMessage(Guid Id, string Type, string Payload, int Attempts, string? TraceParent);

    /// <summary>Publica um lote e devolve quantas mensagens saíram. Zero faz o loop esperar antes do próximo ciclo.</summary>
    public async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var pending = await LockPendingAsync(db, cancellationToken);
        var published = 0;
        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(
                    new OutgoingMessage(message.Id, message.Type, message.Payload, message.TraceParent), cancellationToken);
                await MarkPublishedAsync(db, message.Id, cancellationToken);
                published++;
            }
            catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Broker provavelmente fora: registra a falha já e deixa o resto do lote para o próximo ciclo.
                await MarkFailedAsync(db, message, error, cancellationToken);
                break;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return published;
    }

    public async Task<int> DeletePublishedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var cutoff = time.GetUtcNow() - options.Value.PublishedRetention;
        return await db.Database.ExecuteSqlAsync(
            $"DELETE FROM platform.outbox_messages WHERE status = 'Published' AND published_at < {cutoff}", cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !messaging.Value.IsConfigured)
        {
            LogDisabled();
            return;
        }

        var nextCleanup = time.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                published = await PublishBatchAsync(stoppingToken);
                if (time.GetUtcNow() >= nextCleanup)
                {
                    await DeletePublishedAsync(stoppingToken);
                    nextCleanup = time.GetUtcNow().AddHours(1);
                }
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                LogBatchFailed(error);
            }

            if (published == 0)
            {
                await Task.Delay(options.Value.PollInterval, time, stoppingToken);
            }
        }
    }

    private async Task<List<PendingMessage>> LockPendingAsync(BankingDbContext db, CancellationToken cancellationToken)
    {
        await using var command = DbCommands.InTransaction(
            db,
            """
            SELECT id, type, payload::text, attempts, trace_parent
              FROM platform.outbox_messages
             WHERE status = 'Pending' AND next_attempt_at <= now()
             ORDER BY occurred_at
             LIMIT @batch
               FOR UPDATE SKIP LOCKED
            """);
        command.Parameters.AddWithValue("batch", options.Value.BatchSize);

        var pending = new List<PendingMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pending.Add(new PendingMessage(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return pending;
    }

    private static async Task MarkPublishedAsync(BankingDbContext db, Guid id, CancellationToken cancellationToken)
    {
        await using var command = DbCommands.InTransaction(
            db, "UPDATE platform.outbox_messages SET status = 'Published', published_at = now(), last_error = NULL WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(BankingDbContext db, PendingMessage message, Exception error, CancellationToken cancellationToken)
    {
        var attempts = message.Attempts + 1;
        var deadLettered = attempts >= options.Value.MaxAttempts;
        LogPublishFailed(message.Id, message.Type, attempts, deadLettered, error);

        await using var command = DbCommands.InTransaction(
            db,
            """
            UPDATE platform.outbox_messages
               SET attempts = @attempts, status = @status, next_attempt_at = now() + @delay, last_error = @error
             WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", message.Id);
        command.Parameters.AddWithValue("attempts", attempts);
        command.Parameters.AddWithValue("status", deadLettered ? OutboxStatus.DeadLettered : OutboxStatus.Pending);
        command.Parameters.AddWithValue("delay", RetryDelay(attempts));
        command.Parameters.AddWithValue("error", Truncate($"{error.GetType().Name}: {error.Message}", 500));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Backoff exponencial com jitter, para instâncias não tentarem todas ao mesmo tempo.</summary>
    private TimeSpan RetryDelay(int attempts)
    {
        var exponential = options.Value.BaseRetryDelay * Math.Pow(2, Math.Min(attempts - 1, 16));
        var capped = exponential < options.Value.MaxRetryDelay ? exponential : options.Value.MaxRetryDelay;
        return capped * (0.5 + (Random.Shared.NextDouble() / 2));
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    [LoggerMessage(Level = LogLevel.Information, Message = "Publisher do outbox desligado (Outbox:Enabled ou Messaging:Uri)")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao publicar {MessageId} ({Type}), tentativa {Attempts}, dead-letter: {DeadLettered}")]
    private partial void LogPublishFailed(Guid messageId, string type, int attempts, bool deadLettered, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha no ciclo do publisher do outbox")]
    private partial void LogBatchFailed(Exception error);
}
