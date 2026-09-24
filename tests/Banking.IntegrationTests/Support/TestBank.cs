using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Banking.IntegrationTests.Support;

/// <summary>Monta clientes, contas e depósitos pela API, do jeito que um usuário faria.</summary>
internal sealed class TestBank(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public TestUser Operator { get; } = TestUser.NewOperator();

    public HttpClient Client(TestUser? user) => api.ClientFor(user);

    public async Task<(TestUser User, Guid CustomerId)> NewCustomerAsync(string? cpf = null)
    {
        var user = TestUser.NewCustomer();
        var response = await Client(user).PostAsJsonAsync(
            "/api/v1/customers", new { name = "Cliente de Teste", document = cpf ?? TestCpf.New() }, Ct);
        await EnsureStatusAsync(response, HttpStatusCode.Created);
        return (user, (await ReadAsync(response)).GetProperty("id").GetGuid());
    }

    public async Task<Guid> OpenAccountAsync(TestUser user)
    {
        var response = await Client(user).PostAsJsonAsync("/api/v1/accounts", new { currency = "BRL" }, Ct);
        await EnsureStatusAsync(response, HttpStatusCode.Created);
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    public async Task<(TestUser User, Guid AccountId)> NewAccountAsync(string? initialDeposit = null)
    {
        var (user, _) = await NewCustomerAsync();
        var account = await OpenAccountAsync(user);
        if (initialDeposit is not null)
        {
            await EnsureStatusAsync(await DepositAsync(account, initialDeposit), HttpStatusCode.Created);
        }

        return (user, account);
    }

    public Task<HttpResponseMessage> DepositAsync(
        Guid accountId, string amount, string? idempotencyKey = null, TestUser? asUser = null, string reason = "aporte de teste")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/accounts/{accountId}/deposits")
        {
            Content = JsonContent.Create(new { amount, currency = "BRL", reason }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        return Client(asUser ?? Operator).SendAsync(request, Ct);
    }

    public async Task<string> BalanceAsync(TestUser user, Guid accountId)
    {
        var response = await Client(user).GetAsync($"/api/v1/accounts/{accountId}/balance", Ct);
        await EnsureStatusAsync(response, HttpStatusCode.OK);
        return (await ReadAsync(response)).GetProperty("ledgerBalance").GetString()!;
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    public static async Task EnsureStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.Fail($"Esperado {(int)expected}, veio {(int)response.StatusCode}: {body}");
        }
    }
}
