using System.Security.Cryptography;
using System.Text;
using Banking.Domain.Accounts;

namespace Banking.Infrastructure.Security;

public sealed class PiiOptions
{
    public const string SectionName = "Pii";

    /// <summary>Chave AES-256 em base64. Vem de secret store ou user-secrets, nunca do appsettings.</summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>Chave HMAC-SHA256 em base64, separada da de criptografia.</summary>
    public string BlindIndexKey { get; set; } = string.Empty;
}

/// <summary>
/// AES-GCM com o id do cliente como dado associado: trocar o ciphertext de linha faz a leitura falhar.
/// Formato: versão (1 byte) | nonce (12) | tag (16) | ciphertext.
/// </summary>
internal sealed class AesGcmDocumentProtector : IDocumentProtector
{
    private const byte KeyVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _encryptionKey;
    private readonly byte[] _blindIndexKey;

    public AesGcmDocumentProtector(PiiOptions options)
    {
        _encryptionKey = DecodeKey(options.EncryptionKey, nameof(options.EncryptionKey));
        _blindIndexKey = DecodeKey(options.BlindIndexKey, nameof(options.BlindIndexKey));
    }

    public ProtectedDocument Protect(Cpf cpf, Guid customerId)
    {
        var plaintext = Encoding.ASCII.GetBytes(cpf.Digits);
        var output = new byte[1 + NonceSize + TagSize + plaintext.Length];
        output[0] = KeyVersion;
        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(1 + NonceSize + TagSize), output.AsSpan(1 + NonceSize, TagSize), customerId.ToByteArray());

        return new ProtectedDocument(output, HMACSHA256.HashData(_blindIndexKey, plaintext));
    }

    public Cpf Unprotect(byte[] ciphertext, Guid customerId)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (ciphertext.Length <= 1 + NonceSize + TagSize || ciphertext[0] != KeyVersion)
        {
            throw new CryptographicException("Formato de documento protegido desconhecido.");
        }

        var plaintext = new byte[ciphertext.Length - 1 - NonceSize - TagSize];
        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Decrypt(
            ciphertext.AsSpan(1, NonceSize),
            ciphertext.AsSpan(1 + NonceSize + TagSize),
            ciphertext.AsSpan(1 + NonceSize, TagSize),
            plaintext,
            customerId.ToByteArray());

        return Cpf.Parse(Encoding.ASCII.GetString(plaintext));
    }

    private static byte[] DecodeKey(string base64, string name)
    {
        try
        {
            var key = Convert.FromBase64String(base64);
            return key.Length == 32 ? key : throw new InvalidOperationException($"Pii:{name} precisa ter 32 bytes.");
        }
        catch (FormatException error)
        {
            throw new InvalidOperationException($"Pii:{name} não é base64 válido.", error);
        }
    }
}
