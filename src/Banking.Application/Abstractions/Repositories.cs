using Banking.Domain.Accounts;
using Banking.Domain.Common;
using Banking.Domain.Payments;

namespace Banking.Application.Abstractions;

public interface ICustomerRepository
{
    void Add(Customer customer);

    Task<Customer?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Customer?> FindBySubjectAsync(string subject, CancellationToken cancellationToken);

    Task<bool> ExistsWithDocumentAsync(byte[] blindIndex, CancellationToken cancellationToken);
}

public interface IAccountRepository
{
    void Add(Account account);

    Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Relê do banco. Usado depois de travar o saldo, para decidir com o status atual.</summary>
    Task RefreshAsync(Account account, CancellationToken cancellationToken);

    Task<IReadOnlyList<Account>> ListByCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    Task<Account?> FindByNumberAsync(string branch, string number, CancellationToken cancellationToken);

    Task<long> NextNumberAsync(CancellationToken cancellationToken);
}

public interface IDepositRepository
{
    void Add(Deposit deposit);

    Task<Deposit?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Trava a linha até o fim da transação e devolve o estado atual.</summary>
    Task<Deposit?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Deposit>> ListByStatusAsync(DepositStatus status, int limit, CancellationToken cancellationToken);
}

public interface ITransferReversalRepository
{
    void Add(TransferReversal reversal);

    Task<TransferReversal?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<TransferReversal?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TransferReversal>> ListByStatusAsync(ReversalStatus status, int limit, CancellationToken cancellationToken);
}

public interface IManualResolutionRepository
{
    void Add(ManualResolution resolution);

    Task<ManualResolution?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ManualResolution?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ManualResolution>> ListByStatusAsync(ManualResolutionStatus status, int limit, CancellationToken cancellationToken);
}

public interface ITransferRepository
{
    void Add(InternalTransfer transfer);

    Task<InternalTransfer?> FindAsync(Guid id, CancellationToken cancellationToken);
}

public enum LimitKind
{
    DepositPerOperator,
    TransferPerAccount,
}

public interface ILimitUsageStore
{
    /// <summary>Trava o uso do dia até o fim da transação e devolve o valor já usado.</summary>
    Task<Money> LockUsageAsync(LimitKind kind, string subject, DateOnly date, Currency currency, CancellationToken cancellationToken);

    Task AddUsageAsync(LimitKind kind, string subject, DateOnly date, Money amount, CancellationToken cancellationToken);
}

public sealed record StatementLine(
    Guid TransactionId,
    string Type,
    string Description,
    string Direction,
    Money Amount,
    Money BalanceAfter,
    long Sequence,
    DateTimeOffset PostedAt);

public interface IAccountReadModel
{
    Task<Money?> GetBalanceAsync(Guid ledgerAccountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StatementLine>> GetStatementAsync(
        Guid ledgerAccountId, long? beforeSequence, int limit, CancellationToken cancellationToken);
}
