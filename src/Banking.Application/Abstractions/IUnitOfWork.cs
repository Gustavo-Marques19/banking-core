namespace Banking.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>Abre a transação de banco da operação, com lock_timeout e statement_timeout configurados.</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
