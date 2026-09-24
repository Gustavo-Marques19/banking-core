using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Banking.Infrastructure.Messaging;

/// <summary>Conexões com o broker. Publisher e consumidor usam conexões separadas, como recomenda o RabbitMQ.</summary>
public sealed class RabbitMqConnectionProvider(IOptions<MessagingOptions> options)
{
    public const string DeadLetterExchange = "banking.events.dlx";

    public MessagingOptions Options => options.Value;

    public async Task<IConnection> ConnectAsync(string clientName, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(Options.Uri ?? throw new InvalidOperationException("Messaging:Uri não configurada.")),
            ClientProvidedName = clientName,
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false,
            RequestedConnectionTimeout = Options.ConnectTimeout,
            RequestedHeartbeat = TimeSpan.FromSeconds(10),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Options.ConnectTimeout);
        var connection = await factory.CreateConnectionAsync(timeout.Token);

        await using var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
        await channel.ExchangeDeclareAsync(Options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: timeout.Token);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: timeout.Token);
        return connection;
    }
}
