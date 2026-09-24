namespace Banking.Application.Abstractions;

/// <summary>
/// Grava o evento na mesma transação da operação. Quem publica no broker é o worker do outbox, depois do commit.
/// </summary>
public interface IOutbox
{
    void Enqueue(object integrationEvent, DateTimeOffset occurredAt);
}
