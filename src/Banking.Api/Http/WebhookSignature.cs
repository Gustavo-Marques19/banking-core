using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Banking.Api.Http;

/// <summary>
/// Header "X-Provider-Signature: t={unix},v1={hex}" com HMAC-SHA256 de "{t}.{corpo}". Janela de 5 minutos contra replay
/// (threat model T9).
/// </summary>
public static class WebhookSignature
{
    public const string Header = "X-Provider-Signature";
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, string body, DateTimeOffset timestamp)
    {
        var t = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"t={t},v1={Compute(secret, t, body)}";
    }

    public static bool IsValid(string? header, string secret, string body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(header))
        {
            return false;
        }

        var parts = header.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        if (!parts.TryGetValue("t", out var t) || !parts.TryGetValue("v1", out var signature)
            || !long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var unix))
        {
            return false;
        }

        if ((now - DateTimeOffset.FromUnixTimeSeconds(unix)).Duration() > Tolerance)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Compute(secret, t, body)), Encoding.ASCII.GetBytes(signature));
    }

    private static string Compute(string secret, string timestamp, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
}
