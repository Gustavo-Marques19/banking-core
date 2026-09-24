namespace Banking.Application.Common;

/// <summary>Lock timeout ou deadlock. Houve rollback; repetir com a mesma chave de idempotência é seguro (ADR-004).</summary>
public sealed class ContentionException(string message, Exception innerException) : Exception(message, innerException);

/// <summary>Violação de UNIQUE traduzida da infraestrutura.</summary>
public sealed class UniqueConstraintException(string constraintName, Exception innerException)
    : Exception($"Violação de unicidade em {constraintName}.", innerException)
{
    public string ConstraintName { get; } = constraintName;
}
