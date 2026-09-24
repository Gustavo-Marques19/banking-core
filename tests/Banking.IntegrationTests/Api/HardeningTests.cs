using System.Net;
using System.Net.Http.Json;
using Banking.IntegrationTests.Support;
using Xunit;

namespace Banking.IntegrationTests.Api;

/// <summary>Maker-checker (depósito acima do limite e estorno) e rate limiting por usuário.</summary>
public sealed class HardeningTests(PostgresFixture postgres)
{
    private readonly TestBank _bank = new(postgres.Api);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deposito_acima_do_limite_de_aprovacao_espera_outro_operador()
    {
        var (user, account) = await _bank.NewAccountAsync();
        var checker = TestUser.NewOperator();

        var pending = await _bank.DepositAsync(account, "10000.01");
        Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
        var depositId = (await TestBank.ReadAsync(pending)).GetProperty("id").GetGuid();
        Assert.Equal("0.00", await _bank.BalanceAsync(user, account));

        var selfApproval = await _bank.Client(_bank.Operator).PostAsync($"/api/v1/deposits/{depositId}/approve", null, Ct);
        var asCustomer = await _bank.Client(user).PostAsync($"/api/v1/deposits/{depositId}/approve", null, Ct);
        var approved = await _bank.Client(checker).PostAsync($"/api/v1/deposits/{depositId}/approve", null, Ct);
        var again = await _bank.Client(checker).PostAsync($"/api/v1/deposits/{depositId}/approve", null, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);
        Assert.Equal("self_approval_not_allowed", (await TestBank.ReadAsync(selfApproval)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
        await TestBank.EnsureStatusAsync(approved, HttpStatusCode.OK);
        Assert.Equal(checker.Subject, (await TestBank.ReadAsync(approved)).GetProperty("decidedBy").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("10000.01", await _bank.BalanceAsync(user, account));
    }

    [Fact]
    public async Task Deposito_recusado_pelo_aprovador_nao_cria_dinheiro()
    {
        var (user, account) = await _bank.NewAccountAsync();
        var depositId = (await TestBank.ReadAsync(await _bank.DepositAsync(account, "20000.00"))).GetProperty("id").GetGuid();

        var rejected = await _bank.Client(TestUser.NewOperator()).PostAsync($"/api/v1/deposits/{depositId}/reject", null, Ct);

        await TestBank.EnsureStatusAsync(rejected, HttpStatusCode.OK);
        Assert.Equal("rejected_by_approver", (await TestBank.ReadAsync(rejected)).GetProperty("rejectionReason").GetString());
        Assert.Equal("0.00", await _bank.BalanceAsync(user, account));
    }

    [Fact]
    public async Task Estorno_aprovado_por_outro_operador_devolve_o_dinheiro_com_transacao_ligada()
    {
        var (alice, from) = await _bank.NewAccountAsync("300.00");
        var (bruno, to) = await _bank.NewAccountAsync();
        var transfer = await TestBank.ReadAsync(await Transfers.SendAsync(_bank, alice, from, to, "120.00"));
        var transferId = transfer.GetProperty("id").GetGuid();
        var checker = TestUser.NewOperator();

        var requested = await _bank.Client(_bank.Operator).PostAsJsonAsync($"/api/v1/transfers/{transferId}/reversals", new { reason = "pagamento em duplicidade" }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var reversalId = (await TestBank.ReadAsync(requested)).GetProperty("id").GetGuid();

        var duplicate = await _bank.Client(_bank.Operator).PostAsJsonAsync($"/api/v1/transfers/{transferId}/reversals", new { reason = "de novo" }, Ct);
        var selfApproval = await _bank.Client(_bank.Operator).PostAsync($"/api/v1/reversals/{reversalId}/approve", null, Ct);
        var approved = await _bank.Client(checker).PostAsync($"/api/v1/reversals/{reversalId}/approve", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);
        await TestBank.EnsureStatusAsync(approved, HttpStatusCode.OK);
        Assert.Equal("300.00", await _bank.BalanceAsync(alice, from));
        Assert.Equal("0.00", await _bank.BalanceAsync(bruno, to));

        await using var connection = await Db.OpenAsync(postgres.AppConnectionString);
        Assert.Equal(1L, await Db.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM ledger.ledger_transactions WHERE reverses_transaction_id = @original AND type = 'TransferReversal'",
            null,
            ("original", transfer.GetProperty("ledgerTransactionId").GetGuid())));
    }

    [Fact]
    public async Task Estorno_e_recusado_se_o_destinatario_ja_gastou()
    {
        var (alice, from) = await _bank.NewAccountAsync("100.00");
        var (bruno, to) = await _bank.NewAccountAsync();
        var (_, elsewhere) = await _bank.NewAccountAsync();
        var transferId = (await TestBank.ReadAsync(await Transfers.SendAsync(_bank, alice, from, to, "100.00"))).GetProperty("id").GetGuid();
        await TestBank.EnsureStatusAsync(await Transfers.SendAsync(_bank, bruno, to, elsewhere, "60.00"), HttpStatusCode.Created);

        var requested = await _bank.Client(_bank.Operator).PostAsJsonAsync($"/api/v1/transfers/{transferId}/reversals", new { reason = "contestação" }, Ct);
        var reversalId = (await TestBank.ReadAsync(requested)).GetProperty("id").GetGuid();
        var approved = await _bank.Client(TestUser.NewOperator()).PostAsync($"/api/v1/reversals/{reversalId}/approve", null, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, approved.StatusCode);
        Assert.Equal("insufficient_funds", (await TestBank.ReadAsync(approved)).GetProperty("code").GetString());
        Assert.Equal("40.00", await _bank.BalanceAsync(bruno, to));
        Assert.Equal("0.00", await _bank.BalanceAsync(alice, from));
    }

    [Fact]
    public async Task Rate_limit_por_usuario_responde_429_sem_afetar_outros_nem_o_health()
    {
        await using var api = postgres.CreateApi(new Dictionary<string, string?>
        {
            ["RateLimiting:TokenLimit"] = "3",
            ["RateLimiting:TokensPerPeriod"] = "1",
            ["RateLimiting:ReplenishmentPeriod"] = "00:01:00",
        });
        var noisy = api.ClientFor(TestUser.NewCustomer());
        var quiet = api.ClientFor(TestUser.NewCustomer());

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 4; i++)
        {
            responses.Add(await noisy.GetAsync("/api/v1/accounts", Ct));
        }

        Assert.All(responses.Take(3), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[3].StatusCode);
        Assert.True(responses[3].Headers.RetryAfter is not null);
        Assert.Equal(HttpStatusCode.OK, (await quiet.GetAsync("/api/v1/accounts", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await noisy.GetAsync("/health", Ct)).StatusCode);
    }
}
