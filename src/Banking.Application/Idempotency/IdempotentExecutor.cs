using System.Text.Json;
using Banking.Application.Abstractions;
using Banking.Application.Common;

namespace Banking.Application.Idempotency;

public sealed record IdempotentResult<T>(Result<T> Result, bool Replayed);

/// <summary>
/// Roda a operação na mesma transação da chave de idempotência. Sucesso (inclusive recusa de negócio) é gravado
/// e repetido; erro de validação ou recurso inexistente faz rollback e não consome a chave (ADR-005).
/// </summary>
public sealed class IdempotentExecutor(IUnitOfWork unitOfWork, IIdempotencyStore store)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IdempotentResult<T>> ExecuteAsync<T>(
        IdempotencyRequest request, Func<CancellationToken, Task<Result<T>>> operation, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

            var claim = await store.ClaimAsync(request, cancellationToken);
            switch (claim.Status)
            {
                case IdempotencyClaimStatus.Replay:
                    BankingTelemetry.TransferDuplicate.Add(1, new KeyValuePair<string, object?>("operation", request.Operation));
                    return new(JsonSerializer.Deserialize<T>(claim.StoredResult!, Json)!, Replayed: true);
                case IdempotencyClaimStatus.Mismatch:
                    return new(KeyReused(), Replayed: false);
            }

            var result = await operation(cancellationToken);
            if (!result.IsSuccess)
            {
                return new(result, Replayed: false);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await store.CompleteAsync(request, JsonSerializer.Serialize(result.Value, Json), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(result, Replayed: false);
        }
        catch (UniqueConstraintException error) when (error.ConstraintName.EndsWith("_idempotency_key", StringComparison.Ordinal))
        {
            // A chave expirou da tabela de idempotência, mas a operação original ainda existe.
            return new(KeyReused(), Replayed: false);
        }
    }

    private static Error KeyReused() => Error.Conflict(
        "idempotency_key_reused", "A Idempotency-Key já foi usada com outro pedido.");
}
