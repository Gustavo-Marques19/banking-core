using System.Net;
using Banking.IntegrationTests.Support;
using Xunit;

namespace Banking.IntegrationTests.Api;

/// <summary>
/// Cenários obrigatórios da spec (§13, §14 e §27). Rodam contra Postgres real e com requisições de fato simultâneas.
/// </summary>
[Trait("Category", "Concurrency")]
public sealed class ConcurrencyTests(PostgresFixture postgres) : IAsyncDisposable
{
    // Pool maior que o número de requisições simultâneas: cada uma segura uma conexão enquanto espera o lock.
    private readonly ApiFactory _api = postgres.CreateApi(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Banking"] = postgres.AppConnectionString + ";Maximum Pool Size=150",
        ["Database:LockTimeout"] = "00:00:15",

        // O cenário é justamente um usuário disparando 100 requisições de uma vez.
        ["RateLimiting:TokenLimit"] = "100000",
    });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Duas_transferencias_de_80_com_saldo_100_so_uma_passa()
    {
        var bank = new TestBank(_api);
        var (alice, from) = await bank.NewAccountAsync("100.00");
        var (_, to) = await bank.NewAccountAsync();
        var client = bank.Client(alice);

        var responses = await Task.WhenAll(
            Transfers.Send(client, from, to, "80.00"),
            Transfers.Send(client, from, to, "80.00"));

        var statuses = responses.Select(r => r.StatusCode).Order().ToArray();
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity], statuses);
        Assert.Equal("20.00", await bank.BalanceAsync(alice, from));
    }

    [Fact]
    public async Task Cem_transferencias_de_100_com_saldo_1000_exatamente_dez_passam()
    {
        var bank = new TestBank(_api);
        var (alice, from) = await bank.NewAccountAsync("1000.00");
        var (bruno, to) = await bank.NewAccountAsync();
        var client = bank.Client(alice);

        var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Transfers.Send(client, from, to, "100.00")));

        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(90, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        foreach (var rejected in responses.Where(r => r.StatusCode == HttpStatusCode.UnprocessableEntity))
        {
            Assert.Equal("insufficient_funds", (await TestBank.ReadAsync(rejected)).GetProperty("code").GetString());
        }

        Assert.Equal("0.00", await bank.BalanceAsync(alice, from));
        Assert.Equal("1000.00", await bank.BalanceAsync(bruno, to));
        await LedgerAssertions.AssertConsistentAsync(postgres);
    }

    [Fact]
    public async Task Cem_requisicoes_com_a_mesma_chave_geram_uma_operacao()
    {
        var bank = new TestBank(_api);
        var (alice, from) = await bank.NewAccountAsync("1000.00");
        var (_, to) = await bank.NewAccountAsync();
        var client = bank.Client(alice);

        var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Transfers.Send(client, from, to, "100.00", "mesma-chave")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync(Ct)));
        Assert.Single(bodies.Distinct());
        Assert.Equal(99, responses.Count(r => r.Headers.Contains("Idempotent-Replayed")));
        Assert.Equal("900.00", await bank.BalanceAsync(alice, from));

        await using var connection = await Db.OpenAsync(postgres.AppConnectionString);
        Assert.Equal(1L, await Db.ScalarAsync<long>(
            connection, "SELECT count(*) FROM payments.internal_transfers WHERE source_account_id = @id", null, ("id", from)));
    }

    [Fact]
    public async Task Transferencias_cruzadas_nao_travam()
    {
        var bank = new TestBank(_api);
        var (alice, a) = await bank.NewAccountAsync("1000.00");
        var (bruno, b) = await bank.NewAccountAsync("1000.00");
        var aliceClient = bank.Client(alice);
        var brunoClient = bank.Client(bruno);

        var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(i => i % 2 == 0
            ? Transfers.Send(aliceClient, a, b, "7.00")
            : Transfers.Send(brunoClient, b, a, "3.00")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal("800.00", await bank.BalanceAsync(alice, a));
        Assert.Equal("1200.00", await bank.BalanceAsync(bruno, b));
        await LedgerAssertions.AssertConsistentAsync(postgres);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
