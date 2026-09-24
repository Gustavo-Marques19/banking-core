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

        // O handshake com um broker congelado nem sempre respeita o cancelamento; o prazo é imposto aqui.
        var connecting = factory.CreateConnectionAsync(timeout.Token);
        IConnection connection;
        try
        {
            connection = await connecting.WaitAsync(Options.ConnectTimeout, cancellationToken);
        }
        catch (Exception) when (!connecting.IsCompleted)
        {
            _ = connecting.ContinueWith(
                t => t.IsCompletedSuccessfully ? AbortQuietlyAsync(t.Result) : Task.CompletedTask,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }

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
        // O dispose espera o loop de leitura do socket, que só termina quando os heartbeats falham.
        // Quem chama não precisa esperar isso: a limpeza continua em segundo plano.
        var cleanup = Task.Run(async () =>
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
        });

        await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(1)));
    }
}
