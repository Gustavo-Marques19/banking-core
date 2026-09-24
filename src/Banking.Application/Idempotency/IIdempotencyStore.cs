namespace Banking.Application.Idempotency;

public interface IIdempotencyStore
{
    /// <summary>
    /// Reserva a chave dentro da transação aberta. Se outra requisição com a mesma chave estiver em andamento,
    /// espera o commit dela e devolve o resultado gravado.
    /// </summary>
    Task<IdempotencyClaim> ClaimAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    Task CompleteAsync(IdempotencyRequest request, string resultJson, CancellationToken cancellationToken);
}

public enum IdempotencyClaimStatus
{
    Claimed,
    Replay,
    Mismatch,
}

public sealed record IdempotencyClaim(IdempotencyClaimStatus Status, string? StoredResult = null);
