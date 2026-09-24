using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Banking.Demo;

/// <summary>Operações da demo pela API, como um cliente faria.</summary>
internal sealed class DemoBank(DemoEnvironment environment)
{
    private int _operators;

    public DemoEnvironment Environment => environment;

    public HttpClient Operator { get; } = environment.ClientFor("demo-operator-1", "operator");

    public HttpClient Admin { get; } = environment.ClientFor("demo-admin", "admin");

    public HttpClient NewOperator() => environment.ClientFor($"demo-operator-{Interlocked.Increment(ref _operators) + 1}", "operator");

    public async Task<(HttpClient Client, Guid Account)> NewAccountAsync(string? initialDeposit = null)
    {
        var client = environment.ClientFor("demo-customer-" + Guid.NewGuid().ToString("N")[..12], "customer");
        await EnsureAsync(await client.PostAsJsonAsync("/api/v1/customers", new { name = "Cliente Demo", document = Cpf() }), HttpStatusCode.Created);
        var account = (await ReadAsync(await client.PostAsJsonAsync("/api/v1/accounts", new { currency = "BRL" }))).GetProperty("id").GetGuid();
        if (initialDeposit is not null)
        {
            await EnsureAsync(await DepositAsync(account, initialDeposit), HttpStatusCode.Created);
        }

        return (client, account);
    }

    public Task<HttpResponseMessage> DepositAsync(Guid account, string amount) =>
        Send(Operator, HttpMethod.Post, $"/api/v1/accounts/{account}/deposits", new { amount, currency = "BRL", reason = "aporte da demo" });

    public Task<HttpResponseMessage> TransferAsync(HttpClient client, Guid from, Guid to, string amount, string? key = null) =>
        Send(client, HttpMethod.Post, "/api/v1/transfers", new { sourceAccountId = from, destinationAccountId = to, amount, currency = "BRL" }, key);

    public Task<HttpResponseMessage> ExternalTransferAsync(HttpClient client, Guid from, string amount, string destinationAccount) =>
        Send(client, HttpMethod.Post, "/api/v1/external-transfers", new
        {
            sourceAccountId = from,
            amount,
            currency = "BRL",
            destination = new { bank = "00000000", branch = "0001", account = destinationAccount },
        });

    public async Task<string> BalanceAsync(HttpClient client, Guid account) =>
        (await ReadAsync(await client.GetAsync($"/api/v1/accounts/{account}/balance"))).GetProperty("ledgerBalance").GetString()!;

    public async Task<JsonElement> ReconcileAsync() => await ReadAsync(await Operator.GetAsync("/api/v1/admin/reconciliation"));

    public async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(environment.AppConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task WaitAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("A condição esperada não aconteceu no prazo.");
            }

            await Task.Delay(200);
        }
    }

    private static async Task EnsureAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException($"Esperado {(int)expected}, veio {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, string? key = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }

    /// <summary>CPF aleatório válido. Nenhum CPF real entra na demo.</summary>
    private static string Cpf()
    {
        var digits = new int[11];
        do
        {
            for (var i = 0; i < 9; i++)
            {
                digits[i] = Random.Shared.Next(10);
            }
        }
        while (digits.Take(9).Distinct().Count() == 1);

        for (var length = 9; length <= 10; length++)
        {
            var sum = 0;
            for (var i = 0; i < length; i++)
            {
                sum += digits[i] * (length + 1 - i);
            }

            var remainder = sum * 10 % 11;
            digits[length] = remainder == 10 ? 0 : remainder;
        }

        return string.Concat(digits);
    }
}
