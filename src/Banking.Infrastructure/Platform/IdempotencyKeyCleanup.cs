using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Banking.Infrastructure.Platform;

/// <summary>
/// Remove chaves expiradas em lotes. A chave some daqui, mas continua na tabela da operação (ADR-005).
/// </summary>
public sealed partial class IdempotencyKeyCleanup(IServiceScopeFactory scopes, ILogger<IdempotencyKeyCleanup> logger) : BackgroundService
{
    private const int BatchSize = 1_000;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.Database.ExecuteSqlAsync(
                $"""
                DELETE FROM platform.idempotency_keys
                 WHERE ctid IN (SELECT ctid FROM platform.idempotency_keys WHERE expires_at < now() LIMIT {BatchSize})
                """,
                cancellationToken);
            total += deleted;
        }
        while (deleted == BatchSize);

        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var deleted = await RunOnceAsync(stoppingToken);
                if (deleted > 0)
                {
                    LogDeleted(deleted);
                }
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(error);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removidas {Deleted} chaves de idempotência expiradas")]
    private partial void LogDeleted(int deleted);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao limpar chaves de idempotência")]
    private partial void LogFailed(Exception error);
}
