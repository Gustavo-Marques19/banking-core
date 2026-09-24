using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace Banking.Backoffice.Bff;

/// <summary>
/// Backend-for-frontend do backoffice (ADR-011). Faz o login OIDC, guarda os tokens no servidor e entrega ao navegador
/// só um cookie de sessão. /api é repassado à API com o token do usuário.
/// </summary>
public static class BffProgram
{
    public const string BackofficePolicy = "backoffice";
    private const string AccessTokenItem = "bff.access_token";

    // Main explícito num namespace: top-level statements gerariam um Program global que colide com o da API nos testes.
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var options = builder.Configuration.GetSection(BffOptions.SectionName).Get<BffOptions>() ?? new BffOptions();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient("oidc");
        builder.Services.AddSingleton<TokenRefresher>();

        builder.Services.AddAuthentication(auth =>
            {
                auth.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                auth.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = "__Host-backoffice";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(1);
                cookie.SlidingExpiration = true;
                cookie.Events.OnRedirectToLogin = context => Reject(context, StatusCodes.Status401Unauthorized, "/bff/login");
                cookie.Events.OnRedirectToAccessDenied = context => Reject(context, StatusCodes.Status403Forbidden, "/");
            })
            .AddOpenIdConnect(oidc =>
            {
                oidc.Authority = options.Authority;
                oidc.MetadataAddress = options.MetadataAddress;
                oidc.ClientId = options.ClientId;
                oidc.ClientSecret = options.ClientSecret;
                oidc.RequireHttpsMetadata = options.RequireHttpsMetadata;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.ResponseMode = OpenIdConnectResponseMode.Query;
                oidc.UsePkce = true;
                oidc.SaveTokens = true;
                oidc.MapInboundClaims = false;
                oidc.GetClaimsFromUserInfoEndpoint = false;
                oidc.Scope.Clear();
                oidc.Scope.Add("openid");
                oidc.TokenValidationParameters.NameClaimType = "preferred_username";
                oidc.TokenValidationParameters.RoleClaimType = "roles";
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(BackofficePolicy, policy => policy.RequireAuthenticatedUser().RequireRole("operator", "admin"))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole("operator", "admin").Build());

        var routes = new List<RouteConfig>
        {
            new() { RouteId = "api", ClusterId = "api", AuthorizationPolicy = BackofficePolicy, Match = new RouteMatch { Path = "/api/{**rest}" } },
        };
        var clusters = new List<ClusterConfig>
        {
            new() { ClusterId = "api", Destinations = new Dictionary<string, DestinationConfig> { ["api"] = new() { Address = options.ApiBaseUrl } } },
        };
        if (options.SpaDevServerUrl is { } devServer)
        {
            routes.Add(new RouteConfig { RouteId = "spa", ClusterId = "spa", AuthorizationPolicy = "anonymous", Order = 1000, Match = new RouteMatch { Path = "{**rest}" } });
            clusters.Add(new ClusterConfig { ClusterId = "spa", Destinations = new Dictionary<string, DestinationConfig> { ["spa"] = new() { Address = devServer } } });
        }

        builder.Services.AddReverseProxy()
            .LoadFromMemory(routes, clusters)
            .AddTransforms(transforms =>
            {
                if (transforms.Route.ClusterId != "api")
                {
                    return;
                }

                transforms.AddRequestTransform(context =>
                {
                    // O cookie de sessão não sai do BFF; a API só recebe o token.
                    context.ProxyRequest.Headers.Remove("Cookie");
                    context.ProxyRequest.Headers.Remove(CsrfHeaderMiddleware.Header);
                    context.ProxyRequest.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (string)context.HttpContext.Items[AccessTokenItem]!);
                    return ValueTask.CompletedTask;
                });
            });

        var app = builder.Build();

        app.UseMiddleware<SecurityHeadersMiddleware>();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseMiddleware<CsrfHeaderMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok()).AllowAnonymous();

        app.MapGet("/bff/login", (string? returnUrl, HttpContext http) =>
            {
                var target = returnUrl is not null && IsLocalPath(returnUrl) ? returnUrl : "/";
                return Results.Challenge(new AuthenticationProperties { RedirectUri = target }, [OpenIdConnectDefaults.AuthenticationScheme]);
            })
            .AllowAnonymous();

        app.MapGet("/bff/user", (ClaimsPrincipal user) => Results.Ok(new
        {
            name = user.Identity?.Name,
            subject = user.FindFirstValue("sub"),
            roles = user.FindAll("roles").Select(c => c.Value).Distinct().Order().ToArray(),
        }));

        app.MapPost("/bff/logout", async (HttpContext http, IServiceProvider services) =>
            {
                var authentication = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                var idToken = authentication.Properties?.GetTokenValue("id_token");
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                var oidc = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<OpenIdConnectOptions>>()
                    .Get(OpenIdConnectDefaults.AuthenticationScheme);
                var configuration = await oidc.ConfigurationManager!.GetConfigurationAsync(http.RequestAborted);
                var redirect = $"{http.Request.Scheme}://{http.Request.Host}/";
                var logoutUrl = $"{configuration.EndSessionEndpoint}?client_id={Uri.EscapeDataString(oidc.ClientId!)}"
                    + $"&post_logout_redirect_uri={Uri.EscapeDataString(redirect)}"
                    + (idToken is null ? string.Empty : $"&id_token_hint={Uri.EscapeDataString(idToken)}");
                return Results.Ok(new { logoutUrl });
            })
            .RequireAuthorization(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        app.MapReverseProxy(pipeline => pipeline.Use(async (context, next) =>
        {
            if (context.GetRouteModel().Config.ClusterId == "api")
            {
                var token = await context.RequestServices.GetRequiredService<TokenRefresher>().GetAccessTokenAsync(context);
                if (token is null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                context.Items[AccessTokenItem] = token;
            }

            await next();
        }));

        if (options.SpaDevServerUrl is null)
        {
            app.MapFallbackToFile("index.html").AllowAnonymous();
        }

        app.Run();
    }

    private static Task Reject(RedirectContext<CookieAuthenticationOptions> context, int status, string redirect)
    {
        if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/bff"))
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        }

        context.Response.Redirect(status == StatusCodes.Status401Unauthorized
            ? $"{redirect}?returnUrl={Uri.EscapeDataString(context.Request.Path + context.Request.QueryString)}"
            : redirect);
        return Task.CompletedTask;
    }

    private static bool IsLocalPath(string url) =>
        url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);
}
