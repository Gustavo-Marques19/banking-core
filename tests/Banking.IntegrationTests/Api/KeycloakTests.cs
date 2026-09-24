using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Banking.IntegrationTests.Support;
using Xunit;

namespace Banking.IntegrationTests.Api;

/// <summary>
/// Valida o realm versionado com um Keycloak real: token emitido por ele passa na API com a configuração de produção.
/// </summary>
public sealed class KeycloakTests(PostgresFixture postgres, KeycloakFixture keycloak) : IClassFixture<KeycloakFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Token_do_keycloak_autentica_e_carrega_as_roles()
    {
        var authority = keycloak.Authority;
        await using var api = postgres.CreateApi(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = authority,
            ["Auth:RequireHttpsMetadata"] = "false",
        });

        var customer = api.CreateClient();
        customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await keycloak.PasswordGrantAsync("alice"));
        var customerCallsOperatorEndpoint = await customer.GetAsync($"/api/v1/deposits/{Guid.NewGuid()}", Ct);
        var customerListsAccounts = await customer.GetAsync("/api/v1/accounts", Ct);

        var operatorClient = api.CreateClient();
        operatorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await keycloak.PasswordGrantAsync("olga"));
        var operatorCallsOperatorEndpoint = await operatorClient.GetAsync($"/api/v1/deposits/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.OK, customerListsAccounts.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customerCallsOperatorEndpoint.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, operatorCallsOperatorEndpoint.StatusCode);
    }

    [Fact]
    public async Task Cliente_do_backoffice_exige_pkce_e_nao_aceita_password_grant()
    {
        var authority = keycloak.Authority;
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        var withoutPkce = await http.GetAsync(
            $"{authority}/protocol/openid-connect/auth?client_id=banking-backoffice&response_type=code&scope=openid"
            + "&redirect_uri=" + Uri.EscapeDataString("http://localhost:5180/signin-oidc"),
            Ct);
        var passwordGrant = await http.PostAsync(
            $"{authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "banking-backoffice",
                ["client_secret"] = "backoffice-dev-only",
                ["username"] = "olga",
                ["password"] = "olga-dev-only",
            }),
            Ct);

        Assert.Equal(HttpStatusCode.Found, withoutPkce.StatusCode);
        Assert.Contains("code_challenge", Uri.UnescapeDataString(withoutPkce.Headers.Location!.Query), StringComparison.Ordinal);
        Assert.False(passwordGrant.IsSuccessStatusCode);
    }
}
