using System.Net;
using System.Net.Http.Json;
using Banking.Domain.Ledger;
using Banking.IntegrationTests.Support;
using Xunit;

namespace Banking.IntegrationTests.Api;

public sealed class DepositTests(PostgresFixture postgres)
{
    private readonly TestBank _bank = new(postgres.Api);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deposito_credita_o_cliente_e_debita_o_funding()
    {
        var (user, account) = await _bank.NewAccountAsync();

        var response = await _bank.DepositAsync(account, "1000.00");

        await TestBank.EnsureStatusAsync(response, HttpStatusCode.Created);
        Assert.Equal("1000.00", await _bank.BalanceAsync(user, account));

        var depositId = (await TestBank.ReadAsync(response)).GetProperty("id").GetGuid();
        await using var connection = await Db.OpenAsync(postgres.AppConnectionString);
        var fundingDebit = await Db.ScalarAsync<long>(
            connection,
            """
            SELECT e.amount_minor FROM ledger.ledger_entries e
              JOIN ledger.ledger_transactions t ON t.id = e.transaction_id
             WHERE t.external_id = @externalId AND e.ledger_account_id = @funding AND e.direction = 'D'
            """,
            null,
            ("externalId", $"deposit:{depositId}"),
            ("funding", SystemLedgerAccounts.Funding));
        Assert.Equal(100_000, fundingDebit);
    }

    [Fact]
    public async Task Extrato_mostra_o_credito()
    {
        var (user, account) = await _bank.NewAccountAsync("250.00");

        var statement = await TestBank.ReadAsync(await _bank.Client(user).GetAsync($"/api/v1/accounts/{account}/transactions", Ct));

        var line = Assert.Single(statement.GetProperty("lines").EnumerateArray());
        Assert.Equal("credit", line.GetProperty("direction").GetString());
        Assert.Equal("250.00", line.GetProperty("amount").GetString());
        Assert.Equal("250.00", line.GetProperty("balanceAfter").GetString());
    }

    [Fact]
    public async Task Mesma_chave_e_mesmo_pedido_repetem_a_resposta_sem_depositar_de_novo()
    {
        var (user, account) = await _bank.NewAccountAsync();

        var first = await _bank.DepositAsync(account, "100.00", "chave-1");
        var second = await _bank.DepositAsync(account, "100.0", "chave-1");

        await TestBank.EnsureStatusAsync(second, HttpStatusCode.Created);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(
            (await TestBank.ReadAsync(first)).GetProperty("id").GetGuid(),
            (await TestBank.ReadAsync(second)).GetProperty("id").GetGuid());
        Assert.Equal("100.00", await _bank.BalanceAsync(user, account));
    }

    [Fact]
    public async Task Mesma_chave_com_outro_pedido_responde_409()
    {
        var (_, account) = await _bank.NewAccountAsync();
        await _bank.DepositAsync(account, "100.00", "chave-2");

        var response = await _bank.DepositAsync(account, "200.00", "chave-2");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("idempotency_key_reused", (await TestBank.ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Chave_de_outro_operador_nao_colide()
    {
        var (_, account) = await _bank.NewAccountAsync();
        await _bank.DepositAsync(account, "100.00", "chave-compartilhada");

        var response = await _bank.DepositAsync(account, "300.00", "chave-compartilhada", asUser: TestUser.NewOperator());

        await TestBank.EnsureStatusAsync(response, HttpStatusCode.Created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chave com espaço")]
    public async Task Idempotency_key_ausente_ou_invalida_responde_400(string? key)
    {
        var (_, account) = await _bank.NewAccountAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/accounts/{account}/deposits")
        {
            Content = JsonContent.Create(new { amount = "10.00", currency = "BRL", reason = "teste" }),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        var response = await _bank.Client(_bank.Operator).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("100.001")]
    [InlineData("-10.00")]
    [InlineData("0.00")]
    [InlineData("1e3")]
    public async Task Valor_invalido_responde_400(string amount)
    {
        var (_, account) = await _bank.NewAccountAsync();

        var response = await _bank.DepositAsync(account, amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Valor_como_numero_json_responde_400()
    {
        var (_, account) = await _bank.NewAccountAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/accounts/{account}/deposits")
        {
            Content = new StringContent("""{"amount":100.00,"currency":"BRL","reason":"teste"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", "numero-json");

        var response = await _bank.Client(_bank.Operator).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Acima_do_limite_por_operacao_e_recusado_gravado_e_repetido()
    {
        var (user, account) = await _bank.NewAccountAsync();

        var first = await _bank.DepositAsync(account, "50000.01", "acima-do-limite");
        var replay = await _bank.DepositAsync(account, "50000.01", "acima-do-limite");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, first.StatusCode);
        var body = await TestBank.ReadAsync(first);
        Assert.Equal("limit_exceeded", body.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, replay.StatusCode);
        Assert.Equal(body.GetProperty("depositId").GetGuid(), (await TestBank.ReadAsync(replay)).GetProperty("depositId").GetGuid());
        Assert.Equal("0.00", await _bank.BalanceAsync(user, account));
    }

    [Fact]
    public async Task Limite_diario_por_operador_e_respeitado()
    {
        await using var api = postgres.CreateApi(new Dictionary<string, string?> { ["Limits:DepositDailyPerOperator"] = "150.00" });
        var bank = new TestBank(api);
        var (_, account) = await bank.NewAccountAsync();

        var within = await bank.DepositAsync(account, "100.00");
        var over = await bank.DepositAsync(account, "50.01");

        await TestBank.EnsureStatusAsync(within, HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, over.StatusCode);
        Assert.Equal("daily_limit_exceeded", (await TestBank.ReadAsync(over)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Conta_bloqueada_nao_recebe_deposito()
    {
        var (_, account) = await _bank.NewAccountAsync();
        await _bank.Client(_bank.Operator).PostAsync($"/api/v1/accounts/{account}/block", null, Ct);

        var response = await _bank.DepositAsync(account, "10.00");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("account_blocked", (await TestBank.ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Conta_inexistente_responde_404_e_nao_consome_a_chave()
    {
        var (user, account) = await _bank.NewAccountAsync();

        var missing = await _bank.DepositAsync(Guid.NewGuid(), "10.00", "chave-404");
        var retried = await _bank.DepositAsync(account, "10.00", "chave-404");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await TestBank.EnsureStatusAsync(retried, HttpStatusCode.Created);
        Assert.Equal("10.00", await _bank.BalanceAsync(user, account));
    }
}
