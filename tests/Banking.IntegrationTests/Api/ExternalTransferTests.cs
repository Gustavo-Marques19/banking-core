using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Banking.Api.Http;
using Banking.Domain.Common;
using Banking.Infrastructure.Provider;
using Banking.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Banking.IntegrationTests.Api;

/// <summary>
/// Um teste por cenário do mock (ADR-006). Banco isolado por teste: o worker processa tudo o que encontra.
/// </summary>
[Trait("Category", "ExternalTransfers")]
public sealed class ExternalTransferTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string WebhookSecret = "segredo-de-teste";

    private IsolatedDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await postgres.CreateIsolatedDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sucesso_liquida_uma_vez_e_zera_o_clearing()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "500.00");

        var id = await CreateAsync(bank, alice, account, "200.00", "SUCCESS-1");

        await WaitForStatusAsync(bank, alice, id, "completed");
        Assert.Equal("300.00", await bank.BalanceAsync(alice, account));
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM ledger.ledger_transactions WHERE external_id = @e AND type = 'ExternalTransferSettlement'", Resolution(id)));
        await AssertReconciledAsync(bank);
    }

    [Fact]
    public async Task Recusa_do_provider_estorna_com_transacao_ligada_a_reserva()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "500.00");

        var id = await CreateAsync(bank, alice, account, "200.00", "FAIL-1");

        var view = await WaitForStatusAsync(bank, alice, id, "failed");
        Assert.Equal("destination_account_invalid", view.GetProperty("failureReason").GetString());
        Assert.Equal("500.00", await bank.BalanceAsync(alice, account));
        Assert.Equal(1L, await CountAsync(
            """
            SELECT count(*) FROM ledger.ledger_transactions r
              JOIN ledger.ledger_transactions o ON o.id = r.reverses_transaction_id
             WHERE r.external_id = @e AND o.external_id = @reservation
            """,
            Resolution(id),
            ("reservation", $"external-transfer:{id}:reservation")));
        await AssertReconciledAsync(bank);
    }

    [Fact]
    public async Task Timeout_sem_processamento_fica_unknown_e_vai_para_revisao_sem_virar_failed()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "500.00");

        var id = await CreateAsync(bank, alice, account, "200.00", "TIMEOUT-1");

        await Eventually.TrueAsync(
            async () => (await GetAsync(bank, alice, id)).GetProperty("requiresManualReview").GetBoolean(),
            "revisão manual após tentativas esgotadas");
        var view = await GetAsync(bank, alice, id);
        Assert.Equal("unknown", view.GetProperty("status").GetString());
        Assert.Equal(3, view.GetProperty("submitAttempts").GetInt32());
        Assert.Equal("300.00", await bank.BalanceAsync(alice, account));
        await AssertReconciledAsync(bank);
    }

    [Fact]
    public async Task Timeout_com_processamento_no_provider_conclui_sem_debito_duplo()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "500.00");

        var id = await CreateAsync(bank, alice, account, "200.00", "LATE-1");

        await WaitForStatusAsync(bank, alice, id, "completed");
        Assert.Equal("300.00", await bank.BalanceAsync(alice, account));
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM ledger.ledger_transactions WHERE external_id = @e", Resolution(id)));
        Assert.Equal(0L, await CountAsync(
            "SELECT count(*) FROM ledger.ledger_transactions WHERE external_id = @e AND type = 'ExternalTransferReversal'", Resolution(id)));
        await AssertReconciledAsync(bank);
    }

    [Fact]
    public async Task Http500_no_primeiro_envio_conclui_no_reenvio()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "500.00");

        var id = await CreateAsync(bank, alice, account, "50.00", "HTTP500-1");

        var view = await WaitForStatusAsync(bank, alice, id, "completed");
        Assert.True(view.GetProperty("submitAttempts").GetInt32() >= 2);
        Assert.Equal("450.00", await bank.BalanceAsync(alice, account));
    }

    [Fact]
    public async Task Duplicate_no_provider_conclui()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "500.00");

        var id = await CreateAsync(bank, alice, account, "50.00", "DUPLICATE-1");

        await WaitForStatusAsync(bank, alice, id, "completed");
        Assert.Equal("450.00", await bank.BalanceAsync(alice, account));
    }

    [Fact]
    public async Task Resposta_ambigua_so_vira_failed_depois_da_consulta()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "500.00");

        var id = await CreateAsync(bank, alice, account, "50.00", "UNKNOWN-1");

        var view = await WaitForStatusAsync(bank, alice, id, "failed");
        Assert.Equal("rejected_by_destination_bank", view.GetProperty("failureReason").GetString());
        Assert.Equal("500.00", await bank.BalanceAsync(alice, account));
        await AssertReconciledAsync(bank);
    }

    [Fact]
    public async Task Cancelar_antes_do_envio_estorna_e_nao_cancela_duas_vezes()
    {
        await using var api = Api(worker: false);
        var (bank, alice, account) = await FundedAsync(api, "500.00");
        var id = await CreateAsync(bank, alice, account, "100.00", "SUCCESS-2");
        Assert.Equal("400.00", await bank.BalanceAsync(alice, account));

        var cancelled = await bank.Client(alice).PostAsync($"/api/v1/external-transfers/{id}/cancel", null, Ct);
        var again = await bank.Client(alice).PostAsync($"/api/v1/external-transfers/{id}/cancel", null, Ct);

        await TestBank.EnsureStatusAsync(cancelled, HttpStatusCode.OK);
        Assert.Equal("cancelled", (await TestBank.ReadAsync(cancelled)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("500.00", await bank.BalanceAsync(alice, account));
        await AssertReconciledAsync(bank);
    }

    [Fact]
    public async Task Nao_cancela_depois_do_envio()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "500.00");
        var id = await CreateAsync(bank, alice, account, "100.00", "SUCCESS-3");
        await WaitForStatusAsync(bank, alice, id, "completed");

        var response = await bank.Client(alice).PostAsync($"/api/v1/external-transfers/{id}/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Saldo_insuficiente_e_recusado_sem_reserva()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "10.00");

        var response = await SendAsync(bank, alice, account, "10.01", "SUCCESS-4");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var id = (await TestBank.ReadAsync(response)).GetProperty("externalTransferId").GetGuid();
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM ledger.ledger_transactions WHERE external_id = @e", $"external-transfer:{id}:reservation"));
    }

    [Fact]
    public async Task Cliente_nao_envia_dinheiro_da_conta_de_outro()
    {
        await using var api = Api();
        var (bank, _, victimAccount) = await FundedAsync(api, "500.00");
        var (intruder, _) = await bank.NewAccountAsync();

        var response = await SendAsync(bank, intruder, victimAccount, "100.00", "SUCCESS-5");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_exige_assinatura_valida_e_recente()
    {
        await using var api = Api(worker: false);
        var (bank, alice, account) = await FundedAsync(api, "500.00");
        var id = await CreateAsync(bank, alice, account, "10.00", "SUCCESS-6");
        var body = JsonSerializer.Serialize(new { eventId = Guid.NewGuid(), clientReference = id.ToString("N"), status = "completed" });

        var unsigned = await PostWebhookAsync(api, body, signature: null);
        var wrongSecret = await PostWebhookAsync(api, body, WebhookSignature.Sign("outro-segredo", body, DateTimeOffset.UtcNow));
        var stale = await PostWebhookAsync(api, body, WebhookSignature.Sign(WebhookSecret, body, DateTimeOffset.UtcNow.AddMinutes(-10)));
        var valid = await PostWebhookAsync(api, body, WebhookSignature.Sign(WebhookSecret, body, DateTimeOffset.UtcNow));
        var replay = await PostWebhookAsync(api, body, WebhookSignature.Sign(WebhookSecret, body, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, valid.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM platform.inbox_messages WHERE consumer = @e", "provider-webhook"));

        // O webhook não move dinheiro: a transferência continua CREATED até o worker consultar o provider.
        Assert.Equal("created", (await GetAsync(bank, alice, id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cenario_padrao_do_mock_so_muda_com_admin()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "500.00");
        var admin = TestUser.NewAdmin();

        var asCustomer = await bank.Client(alice).PutAsJsonAsync("/admin/provider/scenario", new { scenario = "FAIL" }, Ct);
        var asAdmin = await bank.Client(admin).PutAsJsonAsync("/admin/provider/scenario", new { scenario = "FAIL" }, Ct);
        var id = await CreateAsync(bank, alice, account, "10.00", "12345");

        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, asAdmin.StatusCode);
        await WaitForStatusAsync(bank, alice, id, "failed");
    }

    [Fact]
    public async Task Reconciliacao_aponta_liquidacao_que_so_o_provider_tem()
    {
        await using var api = Api();
        var (bank, alice, account) = await FundedAsync(api, "500.00");
        var id = await CreateAsync(bank, alice, account, "10.00", "SUCCESS-7");
        await WaitForStatusAsync(bank, alice, id, "completed");
        await AssertReconciledAsync(bank);

        api.Services.GetRequiredService<MockBankingProvider>()
            .RecordUnmatchedSettlement(Guid.NewGuid().ToString("N"), Money.FromMinor(999, Currency.Brl));

        var report = await ReconcileAsync(bank);
        Assert.False(report.GetProperty("isConsistent").GetBoolean());
        Assert.Contains(
            report.GetProperty("findings").EnumerateArray(),
            f => f.GetProperty("check").GetString() == "settlement_missing_in_ledger");
    }

    private ApiFactory Api(bool worker = true) => new(
        _database.AppConnectionString,
        new Dictionary<string, string?>
        {
            ["ExternalTransfers:WorkerEnabled"] = worker.ToString(),
            ["ExternalTransfers:PollInterval"] = "00:00:00.200",
            ["ExternalTransfers:CheckInterval"] = "00:00:00.300",
            ["ExternalTransfers:MaxSubmitAttempts"] = "3",
            ["Provider:Timeout"] = "00:00:00.500",
            ["Provider:WebhookSecret"] = WebhookSecret,
        });

    private static async Task<(TestBank Bank, TestUser User, Guid Account)> FundedAsync(ApiFactory api, string amount)
    {
        var bank = new TestBank(api);
        var (user, account) = await bank.NewAccountAsync(amount);
        return (bank, user, account);
    }

    private static Task<HttpResponseMessage> SendAsync(TestBank bank, TestUser user, Guid account, string amount, string destinationAccount)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/external-transfers")
        {
            Content = JsonContent.Create(new
            {
                sourceAccountId = account,
                amount,
                currency = "BRL",
                destination = new { bank = "00000000", branch = "0001", account = destinationAccount },
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return bank.Client(user).SendAsync(request, Ct);
    }

    private static async Task<Guid> CreateAsync(TestBank bank, TestUser user, Guid account, string amount, string destinationAccount)
    {
        var response = await SendAsync(bank, user, account, amount, destinationAccount);
        await TestBank.EnsureStatusAsync(response, HttpStatusCode.Accepted);
        return (await TestBank.ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GetAsync(TestBank bank, TestUser user, Guid id) =>
        await TestBank.ReadAsync(await bank.Client(user).GetAsync($"/api/v1/external-transfers/{id}", Ct));

    private static async Task<JsonElement> WaitForStatusAsync(TestBank bank, TestUser user, Guid id, string status)
    {
        await Eventually.TrueAsync(async () => (await GetAsync(bank, user, id)).GetProperty("status").GetString() == status, $"status {status}");
        return await GetAsync(bank, user, id);
    }

    private static async Task<JsonElement> ReconcileAsync(TestBank bank)
    {
        var response = await bank.Client(bank.Operator).GetAsync("/api/v1/admin/reconciliation", Ct);
        await TestBank.EnsureStatusAsync(response, HttpStatusCode.OK);
        return await TestBank.ReadAsync(response);
    }

    private static async Task AssertReconciledAsync(TestBank bank)
    {
        var report = await ReconcileAsync(bank);
        Assert.True(report.GetProperty("isConsistent").GetBoolean(), report.GetRawText());
    }

    private static async Task<HttpResponseMessage> PostWebhookAsync(ApiFactory api, string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/provider")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (signature is not null)
        {
            request.Headers.Add(WebhookSignature.Header, signature);
        }

        return await api.CreateClient().SendAsync(request, Ct);
    }

    private static string Resolution(Guid id) => $"external-transfer:{id}:resolution";

    private async Task<long> CountAsync(string sql, string externalId, params (string Name, object Value)[] extra)
    {
        await using var connection = await Db.OpenAsync(_database.AppConnectionString);
        return await Db.ScalarAsync<long>(connection, sql, null, [("e", externalId), .. extra]);
    }
}
