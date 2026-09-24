using System.Security.Cryptography;
using System.Text;

namespace Banking.Application.Idempotency;

/// <summary>
/// Escopo (cliente, operação, chave) e hash do pedido normalizado (ADR-005). O hash usa valores já convertidos,
/// então "100.0" e "100.00" produzem o mesmo resultado.
/// </summary>
public sealed record IdempotencyRequest(string ClientId, string Operation, string Key, byte[] RequestHash)
{
    public const int MaxKeyLength = 64;

    public static IdempotencyRequest Create(string clientId, string operation, string key, params object[] normalizedFields)
    {
        var canonical = string.Join('\u001f', [operation, .. normalizedFields.Select(f => Convert.ToString(f, System.Globalization.CultureInfo.InvariantCulture))]);
        return new IdempotencyRequest(clientId, operation, key, SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key)
        && key.Length <= MaxKeyLength
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
