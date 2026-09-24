using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Banking.IntegrationTests.Support;
using DotNet.Testcontainers.Configurations;
using Testcontainers.Keycloak;
using Xunit;

namespace Banking.IntegrationTests.Api;

/// <summary>
/// Valida o realm versionado com um Keycloak real: token emitido por ele passa na API com a configuração de produção.
/// </summary>
public sealed class KeycloakTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Mesma imagem de .devcontainer/docker-compose.yml.
    private readonly KeycloakContainer _keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.7.4")
        .WithResourceMapping(
            new FileInfo(Path.Combine(RepositoryPaths.Root, ".devcontainer", "keycloak", "realm-banking.json")),
            "/opt/keycloak/data/import/",
            fileMode: UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
        .WithCommand("--import-realm")
        .Build();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _keycloak.StartAsync(Ct);

    public ValueTask DisposeAsync() => _keycloak.DisposeAsync();

    [Fact]
    public async Task Token_do_keycloak_autentica_e_carrega_as_roles()
    {
        var authority = new Uri(new Uri(_keycloak.GetBaseAddress()), "realms/banking").ToString();
        await using var api = postgres.CreateApi(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = authority,
            ["Auth:RequireHttpsMetadata"] = "false",
        });

        var customer = api.CreateClient();
        customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await PasswordGrantAsync(authority, "alice"));
        var customerCallsOperatorEndpoint = await customer.GetAsync($"/api/v1/deposits/{Guid.NewGuid()}", Ct);
        var customerListsAccounts = await customer.GetAsync("/api/v1/accounts", Ct);

        var operatorClient = api.CreateClient();
        operatorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await PasswordGrantAsync(authority, "olga"));
        var operatorCallsOperatorEndpoint = await operatorClient.GetAsync($"/api/v1/deposits/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.OK, customerListsAccounts.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customerCallsOperatorEndpoint.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, operatorCallsOperatorEndpoint.StatusCode);
    }

    private static async Task<string> PasswordGrantAsync(string authority, string username)
    {
        using var http = new HttpClient();
        var response = await http.PostAsync(
            $"{authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "banking-cli",
                ["username"] = username,
                ["password"] = $"{username}-dev-only",
            }),
            Ct);
        await TestBank.EnsureStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("access_token").GetString()!;
    }
}
