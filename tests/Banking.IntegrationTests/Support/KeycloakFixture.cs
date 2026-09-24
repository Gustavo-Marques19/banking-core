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
        .WithEnvironment("CUSTOMER_PUBLIC_URL", CustomerPublicUrl)
        .Build();

    /// <summary>Endereço público do app do cliente neste Keycloak, como o do Codespaces (placeholder do realm).</summary>
    public const string CustomerPublicUrl = "https://app.publico.test";

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

    /// <summary>
    /// Cliente que se comporta como navegador diante do Keycloak em http: o Keycloak marca os cookies de login como
    /// Secure, que um navegador aceita em localhost, mas o CookieContainer do .NET não devolve em http.
    /// </summary>
    public static HttpClient NewBrowser() =>
        new(new CookieJarHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }));

    /// <summary>Preenche o formulário de login do Keycloak como um navegador e devolve para onde ele redireciona.</summary>
    public static async Task<Uri> SubmitLoginFormAsync(HttpClient browser, Uri authorizeUrl, string username)
    {
        var ct = TestContext.Current.CancellationToken;

        // Segue redirects só dentro do próprio Keycloak, até a página de login.
        var page = await browser.GetAsync(authorizeUrl, ct);
        for (var hops = 0; page.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther && hops < 5; hops++)
        {
            var next = new Uri(authorizeUrl, page.Headers.Location!);
            Assert.Equal(authorizeUrl.Authority, next.Authority);
            page = await browser.GetAsync(next, ct);
        }

        var html = await page.Content.ReadAsStringAsync(ct);
        var form = LoginForm().Match(html).Value;
        var action = WebUtility.HtmlDecode(FormAction().Match(form).Groups[1].Value);
        Assert.False(
            string.IsNullOrEmpty(action),
            $"Formulário de login do Keycloak não encontrado. Status {(int)page.StatusCode}; URL {authorizeUrl}; "
            + $"erro: {WebUtility.HtmlDecode(ErrorMessage().Match(html).Groups[1].Value)}");

        var response = await browser.PostAsync(
            new Uri(authorizeUrl, action),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = $"{username}-dev-only" }),
            ct);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return response.Headers.Location!;
    }

    [GeneratedRegex("class=\"(?:instruction|kc-feedback-text)\"[^>]*>([^<]+)<")]
    private static partial Regex ErrorMessage();

    [GeneratedRegex("<form[^>]*kc-form-login[^>]*>")]
    private static partial Regex LoginForm();

    [GeneratedRegex("action=\"([^\"]+)\"")]
    private static partial Regex FormAction();

    private sealed class CookieJarHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly Dictionary<string, string> _jar = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_jar.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _jar.Select(c => $"{c.Key}={c.Value}")));
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    var pair = cookie.Split(';', 2)[0].Split('=', 2);
                    var expired = cookie.Contains("Max-Age=0", StringComparison.OrdinalIgnoreCase);
                    if (pair.Length < 2 || pair[1].Length == 0 || expired)
                    {
                        _jar.Remove(pair[0]);
                    }
                    else
                    {
                        _jar[pair[0]] = pair[1];
                    }
                }
            }

            return response;
        }
    }
}
