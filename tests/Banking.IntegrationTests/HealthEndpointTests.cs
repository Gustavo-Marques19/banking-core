using System.Net;
using Banking.IntegrationTests.Support;
using Xunit;

namespace Banking.IntegrationTests;

public sealed class HealthEndpointTests(PostgresFixture postgres)
{
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=banking;Username=banking_app;Password=unused;Timeout=2";

    [Fact]
    public async Task Health_responde_200_mesmo_sem_banco()
    {
        await using var api = new ApiFactory(UnreachableDatabase);

        var response = await api.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_responde_200_com_banco_disponivel()
    {
        var response = await postgres.Api.CreateClient().GetAsync("/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_responde_503_sem_banco()
    {
        await using var api = new ApiFactory(UnreachableDatabase);

        var response = await api.CreateClient().GetAsync("/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
