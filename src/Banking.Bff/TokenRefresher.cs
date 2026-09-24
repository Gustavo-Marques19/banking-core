using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Banking.Bff;

/// <summary>
/// Renova o access token perto de expirar usando o refresh token, tudo do lado do servidor. Se a renovação falhar,
/// a sessão acaba e o usuário faz login de novo.
/// </summary>
internal sealed class TokenRefresher(IHttpClientFactory httpClients, IOptionsMonitor<OpenIdConnectOptions> oidc, TimeProvider time)
{
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

    public async Task<string?> GetAccessTokenAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Properties is null)
        {
            return null;
        }

        var properties = result.Properties;
        var accessToken = properties.GetTokenValue("access_token");
        var expiresAt = DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

        if (accessToken is not null && expiresAt - Margin > time.GetUtcNow())
        {
            return accessToken;
        }

        var refreshed = await RefreshAsync(properties.GetTokenValue("refresh_token"), context.RequestAborted);
        if (refreshed is null)
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return null;
        }

        properties.UpdateTokenValue("access_token", refreshed.Value.AccessToken);
        properties.UpdateTokenValue("refresh_token", refreshed.Value.RefreshToken);
        properties.UpdateTokenValue(
            "expires_at", time.GetUtcNow().AddSeconds(refreshed.Value.ExpiresIn).ToString("o", CultureInfo.InvariantCulture));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal!, properties);
        return refreshed.Value.AccessToken;
    }

    private async Task<(string AccessToken, string RefreshToken, int ExpiresIn)?> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (refreshToken is null)
        {
            return null;
        }

        var options = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
        using var response = await httpClients.CreateClient("oidc").PostAsync(
            configuration.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        return (root.GetProperty("access_token").GetString()!, root.GetProperty("refresh_token").GetString()!, root.GetProperty("expires_in").GetInt32());
    }
}
