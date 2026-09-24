using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Banking.Infrastructure.Messaging;

public sealed record OutgoingMessage(Guid Id, string RoutingKey, string Payload, string? TraceParent);

public interface IMessagePublisher
{
    /// <summary>Retorna só depois do publisher confirm do broker. Falha lança exceção.</summary>
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}

internal sealed class RabbitMqPublisher(RabbitMqConnectionProvider connections, IOptions<MessagingOptions> options)
    : IMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.PublishTimeout);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(timeout.Token);
            var properties = new BasicProperties
            {
                MessageId = message.Id.ToString(),
                Type = message.RoutingKey,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Headers = message.TraceParent is null ? null : new Dictionary<string, object?> { ["traceparent"] = message.TraceParent },
            };

            // mandatory: sem fila ligada, o broker devolve a mensagem e ela continua pendente no outbox, em vez de sumir.
            await channel.BasicPublishAsync(
                options.Value.Exchange, message.RoutingKey, mandatory: true, properties, Encoding.UTF8.GetBytes(message.Payload), timeout.Token);
        }
        catch (PublishReturnException)
        {
            throw;
        }
        catch
        {
            await ResetAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _gate.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } && _connection is { IsOpen: true })
        {
            return _channel;
        }

        await ResetAsync();
        _connection = await connections.ConnectAsync("banking-outbox-publisher", cancellationToken);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        return _channel;
    }

    private async Task ResetAsync()
    {
        var connection = _connection;
        _channel = null;
        _connection = null;
        if (connection is not null)
        {
            await RabbitMqConnectionProvider.AbortQuietlyAsync(connection);
        }
    }
}
