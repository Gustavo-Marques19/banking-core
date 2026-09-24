using System.Net;
using System.Text;
using System.Text.Json;
using Banking.IntegrationTests.Support;
using Npgsql;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

namespace Banking.IntegrationTests.Api;

/// <summary>
/// Outbox, broker e inbox de ponta a ponta. Cada teste tem broker e banco próprios, porque o publisher processa
/// tudo o que encontra no outbox do banco.
/// </summary>
[Trait("Category", "Messaging")]
public sealed class MessagingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.3.6-management-alpine")
        .WithUsername("banking")
        .WithPassword("test-only")
        .Build();

    private IsolatedDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _rabbit.StartAsync(Ct);
        _database = await postgres.CreateIsolatedDatabaseAsync();
    }

    public ValueTask DisposeAsync() => _rabbit.DisposeAsync();

    [Fact]
    public async Task Transferencia_vira_notificacao_nas_duas_contas()
    {
        await using var api = Api();
        var bank = new TestBank(api);
        var (alice, from) = await bank.NewAccountAsync("100.00");
        var (bruno, to) = await bank.NewAccountAsync();

        await TestBank.EnsureStatusAsync(await Transfers.SendAsync(bank, alice, from, to, "30.00"), HttpStatusCode.Created);

        await Eventually.TrueAsync(() => HasNotificationAsync(bank, alice, from, "transfer_sent"), "transfer_sent na origem");
        await Eventually.TrueAsync(() => HasNotificationAsync(bank, bruno, to, "transfer_received"), "transfer_received no destino");
    }

    [Fact]
    public async Task Broker_fora_do_ar_nao_para_a_operacao_e_o_evento_sai_quando_ele_volta()
    {
        await using var api = Api();
        var bank = new TestBank(api);
        var (alice, from) = await bank.NewAccountAsync("100.00");
        var (bruno, to) = await bank.NewAccountAsync();

        await _rabbit.PauseAsync(Ct);
        var transfer = await Transfers.SendAsync(bank, alice, from, to, "10.00");
        await TestBank.EnsureStatusAsync(transfer, HttpStatusCode.Created);
        var transferId = (await TestBank.ReadAsync(transfer)).GetProperty("id").GetGuid();

        await Eventually.TrueAsync(
            async () => await OutboxStateAsync(transferId) is ("Pending", >= 1),
            "evento pendente com tentativa de publicação falha");

        await _rabbit.UnpauseAsync(Ct);

        await Eventually.TrueAsync(async () => await OutboxStateAsync(transferId) is ("Published", _), "evento publicado após a volta");
        await Eventually.TrueAsync(() => HasNotificationAsync(bank, bruno, to, "transfer_received"), "notificação entregue");
    }

    [Fact]
    public async Task Evento_gravado_antes_da_app_cair_sai_depois_do_restart()
    {
        Guid transferId;
        TestUser bruno;
        Guid to;
        TestBank bank;
        await using (var withoutWorkers = Api(publisher: false, consumer: false))
        {
            bank = new TestBank(withoutWorkers);
            var (alice, from) = await bank.NewAccountAsync("100.00");
            (bruno, to) = await bank.NewAccountAsync();
            var transfer = await Transfers.SendAsync(bank, alice, from, to, "10.00");
            transferId = (await TestBank.ReadAsync(transfer)).GetProperty("id").GetGuid();
        }

        Assert.Equal(("Pending", 0), await OutboxStateAsync(transferId));

        await using var restarted = Api();
        var afterRestart = new TestBank(restarted);
        await Eventually.TrueAsync(async () => await OutboxStateAsync(transferId) is ("Published", _), "evento publicado após o restart");
        await Eventually.TrueAsync(() => HasNotificationAsync(afterRestart, bruno, to, "transfer_received"), "notificação entregue após o restart");
    }

    [Fact]
    public async Task Evento_entregue_mais_de_uma_vez_tem_efeito_unico()
    {
        await using var api = Api();
        var bank = new TestBank(api);
        var (alice, from) = await bank.NewAccountAsync("100.00");
        var (bruno, to) = await bank.NewAccountAsync();
        var transfer = await Transfers.SendAsync(bank, alice, from, to, "10.00");
        var transferId = (await TestBank.ReadAsync(transfer)).GetProperty("id").GetGuid();
        await Eventually.TrueAsync(() => HasNotificationAsync(bank, bruno, to, "transfer_received"), "primeira entrega");

        var (eventId, payload) = await OutboxPayloadAsync(transferId);
        await PublishRawAsync("TransferCompleted.v1", payload);
        await PublishRawAsync("TransferCompleted.v1", payload);

        // Mensagens da mesma fila são processadas em ordem; quando a sentinela chega, as duplicatas já passaram.
        await TestBank.EnsureStatusAsync(await Transfers.SendAsync(bank, alice, from, to, "1.00"), HttpStatusCode.Created);
        await Eventually.TrueAsync(async () => await NotificationCountAsync(to, "transfer_received") == 2, "sentinela processada");

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM platform.inbox_messages WHERE event_id = @id", ("id", eventId)));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM notifications.notifications WHERE event_id = @id", ("id", eventId)));
    }

    [Fact]
    public async Task Mensagem_ilegivel_vai_para_a_dead_letter()
    {
        await using var api = Api();
        var bank = new TestBank(api);
        var (user, account) = await bank.NewAccountAsync("1.00");
        await Eventually.TrueAsync(() => HasNotificationAsync(bank, user, account, "deposit_received"), "consumidor no ar");

        await PublishRawAsync("Lixo.v1", "isto não é json");

        await Eventually.TrueAsync(async () => await DeadLetterCountAsync() == 1, "mensagem na dead-letter");
    }

    private ApiFactory Api(bool publisher = true, bool consumer = true) => new(
        _database.AppConnectionString,
        new Dictionary<string, string?>
        {
            ["Messaging:Uri"] = _rabbit.GetConnectionString(),
            ["Messaging:PublishTimeout"] = "00:00:02",
            ["Messaging:ConnectTimeout"] = "00:00:05",
            ["Outbox:Enabled"] = publisher.ToString(),
            ["Outbox:PollInterval"] = "00:00:00.200",
            ["Outbox:BaseRetryDelay"] = "00:00:00.500",
            ["Outbox:MaxRetryDelay"] = "00:00:02",
            ["Consumers:Enabled"] = consumer.ToString(),
            ["Consumers:ReconnectDelay"] = "00:00:00.500",
        });

    private static async Task<bool> HasNotificationAsync(TestBank bank, TestUser user, Guid account, string kind)
    {
        var response = await bank.Client(user).GetAsync($"/api/v1/accounts/{account}/notifications", Ct);
        var notifications = await TestBank.ReadAsync(response);
        return notifications.EnumerateArray().Any(n => n.GetProperty("kind").GetString() == kind);
    }

    private async Task<(string Status, int Attempts)> OutboxStateAsync(Guid transferId)
    {
        await using var connection = await Db.OpenAsync(_database.AppConnectionString);
        await using var command = new NpgsqlCommand(
            "SELECT status, attempts FROM platform.outbox_messages WHERE type = 'TransferCompleted.v1' AND payload->'data'->>'transferId' = @id",
            connection);
        command.Parameters.AddWithValue("id", transferId.ToString());
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct), "evento da transferência não está no outbox");
        return (reader.GetString(0), reader.GetInt32(1));
    }

    private async Task<(Guid EventId, string Payload)> OutboxPayloadAsync(Guid transferId)
    {
        await using var connection = await Db.OpenAsync(_database.AppConnectionString);
        await using var command = new NpgsqlCommand(
            "SELECT id, payload::text FROM platform.outbox_messages WHERE type = 'TransferCompleted.v1' AND payload->'data'->>'transferId' = @id",
            connection);
        command.Parameters.AddWithValue("id", transferId.ToString());
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        return (reader.GetGuid(0), reader.GetString(1));
    }

    private async Task<long> NotificationCountAsync(Guid account, string kind) =>
        await ScalarAsync("SELECT count(*) FROM notifications.notifications WHERE account_id = @id AND kind = @kind", ("id", account), ("kind", kind));

    private async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await Db.OpenAsync(_database.AppConnectionString);
        return await Db.ScalarAsync<long>(connection, sql, null, parameters);
    }

    private async Task PublishRawAsync(string routingKey, string payload)
    {
        var factory = new ConnectionFactory { Uri = new Uri(_rabbit.GetConnectionString()) };
        await using var connection = await factory.CreateConnectionAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        await channel.BasicPublishAsync("banking.events", routingKey, Encoding.UTF8.GetBytes(payload), Ct);
    }

    private async Task<uint> DeadLetterCountAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(_rabbit.GetConnectionString()) };
        await using var connection = await factory.CreateConnectionAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        try
        {
            return (await channel.QueueDeclarePassiveAsync("banking.notifications.dlq", Ct)).MessageCount;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }
}
