using System.Net;
using System.Net.Http.Json;
using Banking.IntegrationTests.Support;
using Xunit;

namespace Banking.IntegrationTests.Api;

public sealed class TransferTests(PostgresFixture postgres)
{
    private readonly TestBank _bank = new(postgres.Api);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Transferencia_debita_origem_e_credita_destino()
    {
        var (alice, from) = await _bank.NewAccountAsync("500.00");
        var (bruno, to) = await _bank.NewAccountAsync();

        var response = await Transfers.SendAsync(_bank, alice, from, to, "120.50");

        await TestBank.EnsureStatusAsync(response, HttpStatusCode.Created);
        var body = await TestBank.ReadAsync(response);
        Assert.Equal("completed", body.GetProperty("status").GetString());
        Assert.Equal("379.50", await _bank.BalanceAsync(alice, from));
        Assert.Equal("120.50", await _bank.BalanceAsync(bruno, to));

        var ledgerTransaction = await _bank.Client(_bank.Operator).GetAsync(
            $"/api/v1/ledger/transactions/{body.GetProperty("ledgerTransactionId").GetGuid()}", Ct);
        var entries = (await TestBank.ReadAsync(ledgerTransaction)).GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());
    }

    [Fact]
    public async Task Codigo_da_operacao_no_extrato_leva_a_trilha_de_auditoria()
    {
        var (alice, from) = await _bank.NewAccountAsync("100.00");
        var (bruno, to) = await _bank.NewAccountAsync();
        var transferId = (await TestBank.ReadAsync(await Transfers.SendAsync(_bank, alice, from, to, "10.00"))).GetProperty("id").GetGuid();

        var senderLine = (await TestBank.ReadAsync(await _bank.Client(alice).GetAsync($"/api/v1/accounts/{from}/transactions?limit=1", Ct)))
            .GetProperty("lines")[0];
        var receiverLine = (await TestBank.ReadAsync(await _bank.Client(bruno).GetAsync($"/api/v1/accounts/{to}/transactions?limit=1", Ct)))
            .GetProperty("lines")[0];
        var trail = await TestBank.ReadAsync(await _bank.Client(_bank.Operator).GetAsync($"/api/v1/admin/audit?resourceId={transferId}", Ct));

        Assert.Equal(transferId, senderLine.GetProperty("operationId").GetGuid());
        Assert.Equal(transferId, receiverLine.GetProperty("operationId").GetGuid());
        Assert.Contains(trail.EnumerateArray(), e => e.GetProperty("operation").GetString() == "transfer.create");
    }

    [Fact]
    public async Task Saldo_insuficiente_e_recusado_e_gravado()
    {
        var (alice, from) = await _bank.NewAccountAsync("50.00");
        var (_, to) = await _bank.NewAccountAsync();

        var response = await Transfers.SendAsync(_bank, alice, from, to, "50.01");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await TestBank.ReadAsync(response);
        Assert.Equal("insufficient_funds", body.GetProperty("code").GetString());
        var stored = await _bank.Client(alice).GetAsync($"/api/v1/transfers/{body.GetProperty("transferId").GetGuid()}", Ct);
        Assert.Equal("rejected", (await TestBank.ReadAsync(stored)).GetProperty("status").GetString());
        Assert.Equal("50.00", await _bank.BalanceAsync(alice, from));
    }

    [Fact]
    public async Task Transferencia_para_a_propria_conta_responde_400()
    {
        var (alice, from) = await _bank.NewAccountAsync("50.00");

        var response = await Transfers.SendAsync(_bank, alice, from, from, "10.00");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("same_account", (await TestBank.ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cliente_nao_debita_conta_de_outro_e_nada_e_gravado()
    {
        var (victim, victimAccount) = await _bank.NewAccountAsync("1000.00");
        var (intruder, intruderAccount) = await _bank.NewAccountAsync();

        var response = await Transfers.SendAsync(_bank, intruder, victimAccount, intruderAccount, "1000.00", "roubo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("1000.00", await _bank.BalanceAsync(victim, victimAccount));
        await using var connection = await Db.OpenAsync(postgres.AppConnectionString);
        Assert.Equal(0L, await Db.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM payments.internal_transfers WHERE source_account_id = @id",
            null,
            ("id", victimAccount)));
        Assert.Equal(0L, await Db.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM platform.idempotency_keys WHERE client_id = @sub",
            null,
            ("sub", intruder.Subject)));
    }

    [Fact]
    public async Task Destino_bloqueado_e_recusado_sem_dizer_o_motivo_da_outra_conta()
    {
        var (alice, from) = await _bank.NewAccountAsync("100.00");
        var (_, to) = await _bank.NewAccountAsync();
        await _bank.Client(_bank.Operator).PostAsync($"/api/v1/accounts/{to}/block", null, Ct);

        var response = await Transfers.SendAsync(_bank, alice, from, to, "10.00");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("destination_unavailable", (await TestBank.ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Origem_bloqueada_e_recusada()
    {
        var (alice, from) = await _bank.NewAccountAsync("100.00");
        var (_, to) = await _bank.NewAccountAsync();
        await _bank.Client(_bank.Operator).PostAsync($"/api/v1/accounts/{from}/block", null, Ct);

        var response = await Transfers.SendAsync(_bank, alice, from, to, "10.00");

        Assert.Equal("account_blocked", (await TestBank.ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Limites_por_transferencia_e_diario_por_conta()
    {
        await using var api = postgres.CreateApi(new Dictionary<string, string?>
        {
            ["Limits:TransferPerTransaction"] = "100.00",
            ["Limits:TransferDailyPerAccount"] = "150.00",
        });
        var bank = new TestBank(api);
        var (alice, from) = await bank.NewAccountAsync("1000.00");
        var (_, to) = await bank.NewAccountAsync();

        var tooBig = await Transfers.SendAsync(bank, alice, from, to, "100.01");
        var first = await Transfers.SendAsync(bank, alice, from, to, "100.00");
        var overDaily = await Transfers.SendAsync(bank, alice, from, to, "50.01");

        Assert.Equal("limit_exceeded", (await TestBank.ReadAsync(tooBig)).GetProperty("code").GetString());
        await TestBank.EnsureStatusAsync(first, HttpStatusCode.Created);
        Assert.Equal("daily_limit_exceeded", (await TestBank.ReadAsync(overDaily)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Terceiro_nao_ve_a_transferencia_mas_o_destinatario_ve()
    {
        var (alice, from) = await _bank.NewAccountAsync("100.00");
        var (bruno, to) = await _bank.NewAccountAsync();
        var (stranger, _) = await _bank.NewCustomerAsync();
        var transferId = (await TestBank.ReadAsync(await Transfers.SendAsync(_bank, alice, from, to, "1.00"))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await _bank.Client(bruno).GetAsync($"/api/v1/transfers/{transferId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _bank.Client(stranger).GetAsync($"/api/v1/transfers/{transferId}", Ct)).StatusCode);
    }
}

internal static class Transfers
{
    public static Task<HttpResponseMessage> SendAsync(
        TestBank bank, TestUser user, Guid from, Guid to, string amount, string? idempotencyKey = null) =>
        Send(bank.Client(user), from, to, amount, idempotencyKey);

    public static Task<HttpResponseMessage> Send(HttpClient client, Guid from, Guid to, string amount, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transfers")
        {
            Content = JsonContent.Create(new { sourceAccountId = from, destinationAccountId = to, amount, currency = "BRL" }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
