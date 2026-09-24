using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Configurations;
using Testcontainers.Keycloak;
using Xunit;

namespace Banking.IntegrationTests.Support;

/// <summary>Keycloak real com o realm versionado, um por classe de teste.</summary>
public sealed partial class KeycloakFixture : IAsyncLifetime
{
    // Mesma imagem de .devcontainer/docker-compose.yml.
    private readonly KeycloakContainer _container = new KeycloakBuilder("quay.io/keycloak/keycloak:26.7.4")
        .WithResourceMapping(
            new FileInfo(Path.Combine(RepositoryPaths.Root, ".devcontainer", "keycloak", "realm-banking.json")),
            "/opt/keycloak/data/import/",
            fileMode: UnixFileModes.UserRead | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
        .WithCommand("--import-realm")
        .Build();

    public string Authority => new Uri(new Uri(_container.GetBaseAddress()), "realms/banking").ToString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task<string> PasswordGrantAsync(string username)
    {
        using var http = new HttpClient();
        var response = await http.PostAsync(
            $"{Authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "banking-cli",
                ["username"] = username,
                ["password"] = $"{username}-dev-only",
            }),
            TestContext.Current.CancellationToken);
        await TestBank.EnsureStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("access_token").GetString()!;
    }

    /// <summary>Preenche o formulário de login do Keycloak como um navegador e devolve para onde ele redireciona.</summary>
    public static async Task<Uri> SubmitLoginFormAsync(HttpClient browser, Uri authorizeUrl, string username)
    {
        var ct = TestContext.Current.CancellationToken;
        var page = await browser.GetAsync(authorizeUrl, ct);
        var html = await page.Content.ReadAsStringAsync(ct);
        var action = WebUtility.HtmlDecode(FormAction().Match(html).Groups[1].Value);
        Assert.False(string.IsNullOrEmpty(action), "Formulário de login do Keycloak não encontrado.");

        var response = await browser.PostAsync(
            action,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = $"{username}-dev-only" }),
            ct);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return response.Headers.Location!;
    }

    [GeneratedRegex("<form[^>]+id=\"kc-form-login\"[^>]+action=\"([^\"]+)\"")]
    private static partial Regex FormAction();
}
