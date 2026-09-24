namespace Banking.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Tempo máximo esperando lock de saldo. Estourou, a operação volta 503 e pode ser repetida (ADR-004).</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public TimeSpan StatementTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
