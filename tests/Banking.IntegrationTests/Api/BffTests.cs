using System.Net;
using System.Text.Json;
using Banking.Backoffice.Bff;
using Banking.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Banking.IntegrationTests.Api;

/// <summary>
/// Login real no Keycloak, BFF e API no processo (ADR-011). Prova que o navegador só recebe cookie, que CSRF é barrado
/// e que só operador e admin entram.
/// </summary>
public sealed class BffTests(PostgresFixture postgres, KeycloakFixture keycloak) : IClassFixture<KeycloakFixture>, IAsyncLifetime
{
    private ApiFactory _api = null!;
    private BffFactory _bff = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = postgres.CreateApi(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = keycloak.Authority,
            ["Auth:RequireHttpsMetadata"] = "false",
        });
        _bff = new BffFactory(keycloak.Authority, _api);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _bff.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Operador_entra_e_o_navegador_so_recebe_cookie_de_sessao()
    {
        var (browser, cookieHeaders) = await LoginAsync("olga");

        var user = await browser.GetAsync("/bff/user", Ct);
        var body = await user.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, user.StatusCode);
        Assert.Contains("operator", JsonDocument.Parse(body).RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.DoesNotContain("eyJ", body, StringComparison.Ordinal);
        var session = Assert.Single(cookieHeaders, h => h.StartsWith("__Host-backoffice=", StringComparison.Ordinal));
        Assert.DoesNotContain("eyJ", session, StringComparison.Ordinal);
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", session, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Api_via_bff_recebe_o_token_do_usuario()
    {
        var (browser, _) = await LoginAsync("olga");

        var response = await browser.GetAsync("/api/v1/operations/pending-deposits", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Requisicao_que_muda_estado_sem_header_csrf_e_barrada()
    {
        var (browser, _) = await LoginAsync("olga");
        var path = $"/api/v1/deposits/{Guid.NewGuid()}/approve";

        var withoutHeader = await browser.PostAsync(path, null, Ct);
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF", "1");
        var withHeader = await browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, withoutHeader.StatusCode);
        Assert.Contains("csrf_header_required", await withoutHeader.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, withHeader.StatusCode);
    }

    [Fact]
    public async Task Cliente_nao_entra_no_backoffice()
    {
        var (browser, _) = await LoginAsync("alice");

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/bff/user", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/v1/operations/pending-deposits", Ct)).StatusCode);
    }

    [Fact]
    public async Task Sem_sessao_responde_401_em_vez_de_redirecionar()
    {
        var browser = NewBrowser();

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/operations/pending-deposits", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/bff/user", Ct)).StatusCode);
    }

    [Fact]
    public async Task Respostas_trazem_csp_e_headers_de_seguranca()
    {
        var response = await NewBrowser().GetAsync("/health", Ct);

        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Logout_encerra_a_sessao_e_aponta_para_o_logout_do_keycloak()
    {
        var (browser, _) = await LoginAsync("olga");
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.Add("X-CSRF", "1");

        var logout = await browser.SendAsync(request, Ct);
        var logoutUrl = JsonDocument.Parse(await logout.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("logoutUrl").GetString()!;

        Assert.Contains("/protocol/openid-connect/logout", logoutUrl, StringComparison.Ordinal);
        Assert.Contains("id_token_hint=", logoutUrl, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/bff/user", Ct)).StatusCode);
    }

    private HttpClient NewBrowser() => _bff.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    /// <summary>Fluxo de navegador: BFF → Keycloak (formulário) → callback do BFF. Devolve os Set-Cookie do callback.</summary>
    private async Task<(HttpClient Browser, IReadOnlyList<string> CallbackCookies)> LoginAsync(string username)
    {
        var browser = NewBrowser();
        var login = await browser.GetAsync("/bff/login?returnUrl=/", Ct);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        using var keycloakBrowser = KeycloakFixture.NewBrowser();
        var callback = await KeycloakFixture.SubmitLoginFormAsync(keycloakBrowser, login.Headers.Location!, username);
        Assert.Equal("localhost", callback.Host);

        var completed = await browser.GetAsync(callback.PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        return (browser, completed.Headers.TryGetValues("Set-Cookie", out var cookies) ? [.. cookies] : []);
    }

    private sealed class BffFactory(string authority, ApiFactory api) : WebApplicationFactory<BffOptions>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Bff:Authority", authority);
            builder.UseSetting("Bff:ClientSecret", "backoffice-dev-only");
            builder.UseSetting("Bff:RequireHttpsMetadata", "false");
            builder.UseSetting("Bff:ApiBaseUrl", "http://api.internal");

            // A API roda no mesmo processo (TestServer): o proxy fala com ela pelo handler em memória.
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IForwarderHttpClientFactory>(new InMemoryForwarderClientFactory(api)));
        }
    }

    private sealed class InMemoryForwarderClientFactory(ApiFactory api) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(api.Server.CreateHandler(), disposeHandler: false);
    }
}
