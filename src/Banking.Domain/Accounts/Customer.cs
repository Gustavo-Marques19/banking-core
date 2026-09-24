using Banking.Domain.Common;

namespace Banking.Domain.Accounts;

public sealed class Customer
{
    private const int MaxNameLength = 120;

    private Customer()
    {
        Subject = string.Empty;
        Name = string.Empty;
        DocumentCiphertext = [];
        DocumentBlindIndex = [];
    }

    public Guid Id { get; private set; }

    /// <summary>`sub` do token no Keycloak. Um cliente por identidade.</summary>
    public string Subject { get; private set; }

    public string Name { get; private set; }

    public byte[] DocumentCiphertext { get; private set; }

    public byte[] DocumentBlindIndex { get; private set; }

    public CustomerStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Customer Register(string subject, string name, Cpf document, IDocumentProtector protector, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(protector);

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            throw new DomainException("invalid_name", $"Nome precisa ter entre 1 e {MaxNameLength} caracteres.");
        }

        var id = Guid.CreateVersion7(now);
        var protectedDocument = protector.Protect(document, id);

        return new Customer
        {
            Id = id,
            Subject = subject,
            Name = trimmed,
            DocumentCiphertext = protectedDocument.Ciphertext,
            DocumentBlindIndex = protectedDocument.BlindIndex,
            Status = CustomerStatus.Active,
            CreatedAt = now,
        };
    }

    public Cpf RevealDocument(IDocumentProtector protector) => protector.Unprotect(DocumentCiphertext, Id);
}
