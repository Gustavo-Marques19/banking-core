namespace Banking.Domain.Common;

/// <summary>Violação de invariante do domínio. <see cref="Code"/> é estável e vai para a API.</summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
