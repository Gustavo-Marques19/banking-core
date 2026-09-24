using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Banking.IntegrationTests;

public sealed class HealthEndpointTests(PostgresFixture postgres)
{
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=banking;Username=banking_app;Password=unused;Timeout=2";

    [Fact]
    public async Task Health_responde_200_mesmo_sem_banco()
    {
        var response = await GetAsync(UnreachableDatabase, "/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_responde_200_com_banco_disponivel()
    {
        var response = await GetAsync(postgres.AppConnectionString, "/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_responde_503_sem_banco()
    {
        var response = await GetAsync(UnreachableDatabase, "/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> GetAsync(string connectionString, string path)
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Banking", connectionString));
        using var client = factory.CreateClient();

        return await client.GetAsync(path, TestContext.Current.CancellationToken);
    }
}
