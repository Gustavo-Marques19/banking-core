namespace Banking.Domain.Accounts;

/// <summary>
/// Criptografa o CPF para gravação e gera o blind index usado em busca e unicidade (LGPD, threat model T11).
/// </summary>
public interface IDocumentProtector
{
    ProtectedDocument Protect(Cpf cpf, Guid customerId);

    Cpf Unprotect(byte[] ciphertext, Guid customerId);
}

public sealed record ProtectedDocument(byte[] Ciphertext, byte[] BlindIndex);
