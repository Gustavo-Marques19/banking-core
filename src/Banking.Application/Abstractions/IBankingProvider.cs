using Banking.Domain.Common;
using Banking.Domain.Payments;

namespace Banking.Application.Abstractions;

public sealed record ProviderTransferRequest(string ClientReference, Money Amount, ExternalDestination Destination);

public sealed record ProviderStatementLine(string ClientReference, Money Amount, DateTimeOffset SettledAt);

/// <summary>
/// Fronteira com o BaaS (ADR-006). O domínio só vê os resultados; timeout, HTTP e circuit breaker ficam na implementação.
/// A Fase 2 troca o mock por um provider real sem mexer aqui.
/// </summary>
public interface IBankingProvider
{
    Task<SubmitResult> SubmitTransferAsync(ProviderTransferRequest request, CancellationToken cancellationToken);

    Task<ProviderStatusResult> GetTransferStatusAsync(string clientReference, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderStatementLine>> GetStatementAsync(DateOnly date, CancellationToken cancellationToken);
}

/// <summary>Provider fora ou lento demais para responder uma consulta. Nada muda; a consulta é reagendada.</summary>
public sealed class ProviderUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

public interface IExternalTransferRepository
{
    void Add(ExternalTransfer transfer);

    /// <summary>Sem lock, sem rastreamento. Para leitura e para decidir se vale consultar o provider.</summary>
    Task<ExternalTransfer?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Trava a linha até o fim da transação e devolve o estado atual do banco.</summary>
    Task<ExternalTransfer?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Trava até <paramref name="limit"/> transferências em CREATED, pulando as travadas por outra instância.</summary>
    Task<IReadOnlyList<ExternalTransfer>> LockCreatedAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListDueForCheckAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
}

public interface IInbox
{
    /// <summary>Registra o evento na transação aberta. Devolve false se ele já tinha sido processado.</summary>
    Task<bool> TryRecordAsync(string consumer, Guid eventId, CancellationToken cancellationToken);
}
