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

        try
        {
            var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
            await channel.ExchangeDeclareAsync(Options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: timeout.Token);
            await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: timeout.Token);
            await channel.CloseAsync(timeout.Token);
            return connection;
        }
        catch
        {
            await AbortQuietlyAsync(connection);
            throw;
        }
    }

    /// <summary>
    /// Fecha sem esperar resposta do broker. Com o broker congelado, o fechamento normal esperaria dezenas de segundos.
    /// </summary>
    public static async Task AbortQuietlyAsync(IConnection connection)
    {
        try
        {
            await connection.AbortAsync(TimeSpan.FromSeconds(1));
            await connection.DisposeAsync();
        }
        catch (Exception)
        {
            // A conexão já está sendo descartada; um erro no fechamento não muda nada.
        }
    }
}
