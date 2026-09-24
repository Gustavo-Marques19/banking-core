using System.Net;
using System.Net.Http.Json;
using Banking.IntegrationTests.Support;
using Xunit;

namespace Banking.IntegrationTests.Api;

public sealed class CustomerAndAccountTests(PostgresFixture postgres)
{
    private readonly TestBank _bank = new(postgres.Api);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cadastro_devolve_cpf_mascarado()
    {
        var cpf = TestCpf.New();
        var user = TestUser.NewCustomer();

        var response = await _bank.Client(user).PostAsJsonAsync(
            "/api/v1/customers", new { name = "Maria", document = TestCpf.Formatted(cpf) }, Ct);

        await TestBank.EnsureStatusAsync(response, HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(cpf, body);
        Assert.Contains($"***.{cpf[3..6]}.{cpf[6..9]}-**", body);
    }

    [Fact]
    public async Task Cpf_fica_criptografado_no_banco()
    {
        var cpf = TestCpf.New();
        var (_, customerId) = await _bank.NewCustomerAsync(cpf);

        await using var connection = await Db.OpenAsync(postgres.AppConnectionString);
        var stored = await Db.ScalarAsync<byte[]>(
            connection, "SELECT document_ciphertext FROM accounts.customers WHERE id = @id", null, ("id", customerId));

        Assert.DoesNotContain(cpf, System.Text.Encoding.ASCII.GetString(stored));
    }

    [Fact]
    public async Task Mesma_identidade_ou_mesmo_cpf_da_o_mesmo_conflito()
    {
        var cpf = TestCpf.New();
        var (user, _) = await _bank.NewCustomerAsync(cpf);

        var sameIdentity = await _bank.Client(user).PostAsJsonAsync(
            "/api/v1/customers", new { name = "Outro", document = TestCpf.New() }, Ct);
        var sameCpf = await _bank.Client(TestUser.NewCustomer()).PostAsJsonAsync(
            "/api/v1/customers", new { name = "Outro", document = cpf }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, sameIdentity.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sameCpf.StatusCode);
        Assert.Equal(
            (await TestBank.ReadAsync(sameIdentity)).GetProperty("code").GetString(),
            (await TestBank.ReadAsync(sameCpf)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("123.456.789-00")]
    [InlineData("11111111111")]
    [InlineData("abc")]
    public async Task Cpf_invalido_responde_400(string document)
    {
        var response = await _bank.Client(TestUser.NewCustomer()).PostAsJsonAsync(
            "/api/v1/customers", new { name = "Maria", document }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Campo_desconhecido_no_json_responde_400()
    {
        var response = await _bank.Client(TestUser.NewCustomer()).PostAsJsonAsync(
            "/api/v1/customers", new { name = "Maria", document = TestCpf.New(), status = "Blocked" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Abrir_conta_sem_cadastro_responde_409()
    {
        var response = await _bank.Client(TestUser.NewCustomer()).PostAsJsonAsync("/api/v1/accounts", new { currency = "BRL" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Conta_nova_tem_saldo_zero_e_extrato_vazio()
    {
        var (user, account) = await _bank.NewAccountAsync();

        Assert.Equal("0.00", await _bank.BalanceAsync(user, account));
        var statement = await TestBank.ReadAsync(await _bank.Client(user).GetAsync($"/api/v1/accounts/{account}/transactions", Ct));
        Assert.Equal(0, statement.GetProperty("lines").GetArrayLength());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Extrato_com_pagina_fora_do_limite_responde_400(int limit)
    {
        var (user, account) = await _bank.NewAccountAsync();

        var response = await _bank.Client(user).GetAsync($"/api/v1/accounts/{account}/transactions?limit={limit}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cliente_lista_so_as_proprias_contas()
    {
        var (user, account) = await _bank.NewAccountAsync();
        await _bank.NewAccountAsync();

        var accounts = await TestBank.ReadAsync(await _bank.Client(user).GetAsync("/api/v1/accounts", Ct));

        Assert.Equal(account, Assert.Single(accounts.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("/api/v1/accounts/{0}")]
    [InlineData("/api/v1/accounts/{0}/balance")]
    [InlineData("/api/v1/accounts/{0}/transactions")]
    public async Task Cliente_nao_ve_conta_de_outro(string path)
    {
        var (_, account) = await _bank.NewAccountAsync("100.00");
        var (intruder, _) = await _bank.NewCustomerAsync();

        var response = await _bank.Client(intruder).GetAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, account), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cliente_nao_ve_cadastro_de_outro_mas_operador_ve()
    {
        var (_, customerId) = await _bank.NewCustomerAsync();
        var (intruder, _) = await _bank.NewCustomerAsync();

        var asIntruder = await _bank.Client(intruder).GetAsync($"/api/v1/customers/{customerId}", Ct);
        var asOperator = await _bank.Client(_bank.Operator).GetAsync($"/api/v1/customers/{customerId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, asIntruder.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asOperator.StatusCode);
    }

    [Fact]
    public async Task Operador_bloqueia_e_desbloqueia_conta()
    {
        var (user, account) = await _bank.NewAccountAsync();

        var blocked = await _bank.Client(_bank.Operator).PostAsync($"/api/v1/accounts/{account}/block", null, Ct);
        Assert.Equal("blocked", (await TestBank.ReadAsync(blocked)).GetProperty("status").GetString());

        var unblocked = await _bank.Client(_bank.Operator).PostAsync($"/api/v1/accounts/{account}/unblock", null, Ct);
        Assert.Equal("active", (await TestBank.ReadAsync(unblocked)).GetProperty("status").GetString());

        var asCustomer = await _bank.Client(user).PostAsync($"/api/v1/accounts/{account}/block", null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
    }

    [Fact]
    public async Task Busca_por_agencia_e_numero_devolve_o_id_e_o_titular_mascarado()
    {
        var (owner, account) = await _bank.NewAccountAsync();
        var number = (await TestBank.ReadAsync(await _bank.Client(owner).GetAsync($"/api/v1/accounts/{account}", Ct))).GetProperty("number").GetString()!;
        var (someone, _) = await _bank.NewCustomerAsync();

        var response = await _bank.Client(someone).GetAsync($"/api/v1/accounts/lookup?branch=0001&number={number.TrimStart('0')}", Ct);

        await TestBank.EnsureStatusAsync(response, HttpStatusCode.OK);
        var body = await TestBank.ReadAsync(response);
        Assert.Equal(account, body.GetProperty("id").GetGuid());
        Assert.Equal(number, body.GetProperty("number").GetString());
        Assert.Equal("Cliente T.", body.GetProperty("holderName").GetString());
        Assert.False(body.TryGetProperty("customerId", out _));
        Assert.False(body.TryGetProperty("status", out _));
    }

    [Theory]
    [InlineData("0001", "99999999", HttpStatusCode.NotFound)]
    [InlineData("1", "12", HttpStatusCode.BadRequest)]
    [InlineData("0001", "12a", HttpStatusCode.BadRequest)]
    [InlineData("0001", "123456789", HttpStatusCode.BadRequest)]
    public async Task Busca_invalida_ou_sem_resultado(string branch, string number, HttpStatusCode expected)
    {
        var (user, _) = await _bank.NewCustomerAsync();

        var response = await _bank.Client(user).GetAsync($"/api/v1/accounts/lookup?branch={branch}&number={number}", Ct);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Busca_de_conta_tem_limite_proprio_por_usuario()
    {
        await using var api = postgres.CreateApi(new Dictionary<string, string?> { ["RateLimiting:LookupsPerMinute"] = "2" });
        var bank = new TestBank(api);
        var (scanner, _) = await bank.NewCustomerAsync();
        var (other, _) = await bank.NewCustomerAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 1; i <= 3; i++)
        {
            statuses.Add((await bank.Client(scanner).GetAsync($"/api/v1/accounts/lookup?branch=0001&number=9999999{i}", Ct)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.NotFound, (await bank.Client(other).GetAsync("/api/v1/accounts/lookup?branch=0001&number=99999991", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bank.Client(scanner).GetAsync("/api/v1/accounts", Ct)).StatusCode);
    }
}
