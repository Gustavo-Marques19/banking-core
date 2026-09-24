using Banking.Application.ExternalTransfers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Provider;

/// <summary>Envia transferências em CREATED e consulta as que estão em UNKNOWN ou PROCESSING. Um escopo por ciclo.</summary>
public sealed partial class ExternalTransferWorker(
    IServiceScopeFactory scopes,
    IOptions<ExternalTransferWorkerOptions> options,
    TimeProvider time,
    ILogger<ExternalTransferWorker> logger) : BackgroundService
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<ExternalTransferProcessor>();
        var submitted = await processor.SubmitPendingAsync(options.Value.BatchSize, cancellationToken);

        await using var reconcileScope = scopes.CreateAsyncScope();
        var reconciler = reconcileScope.ServiceProvider.GetRequiredService<ExternalTransferProcessor>();
        var checkedCount = await reconciler.ReconcileDueAsync(options.Value.BatchSize, cancellationToken);
        return submitted + checkedCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
        {
            LogDisabled();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await RunOnceAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                LogCycleFailed(error);
            }

            if (processed == 0)
            {
                await Task.Delay(options.Value.PollInterval, time, stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker de transferências externas desligado (ExternalTransfers:WorkerEnabled)")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha no ciclo do worker de transferências externas")]
    private partial void LogCycleFailed(Exception error);
}
