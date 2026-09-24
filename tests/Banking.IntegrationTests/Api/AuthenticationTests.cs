using System.Net;
using System.Net.Http.Headers;
using Banking.IntegrationTests.Support;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Banking.IntegrationTests.Api;

public sealed class AuthenticationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sem_token_responde_401()
    {
        var response = await postgres.Api.ClientFor(null).GetAsync("/api/v1/accounts", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static TheoryData<string> InvalidTokens => new()
    {
        "outra-audience",
        "expirado",
        "chave-desconhecida",
        "hmac-em-vez-de-rsa",
        "sem-assinatura",
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task Token_invalido_responde_401(string kind)
    {
        var user = TestUser.NewCustomer();
        var token = kind switch
        {
            "outra-audience" => TestTokens.For(user, audience: "outra-api"),
            "expirado" => TestTokens.For(user, expires: DateTime.UtcNow.AddMinutes(-5)),
            "chave-desconhecida" => TestTokens.For(user, key: new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048))),
            "hmac-em-vez-de-rsa" => TestTokens.For(user, key: new SymmetricSecurityKey(new byte[32])),
            _ => Unsigned(TestTokens.For(user)),
        };

        var client = postgres.Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/accounts", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cliente_em_endpoint_de_operador_responde_403()
    {
        var bank = new TestBank(postgres.Api);
        var (user, account) = await bank.NewAccountAsync();

        var response = await bank.DepositAsync(account, "10.00", asUser: user);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static string Unsigned(string token)
    {
        var parts = token.Split('.');
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        return $"{header}.{parts[1]}.";
    }
}
