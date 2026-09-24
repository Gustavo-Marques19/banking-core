using Banking.Application.Common;
using Banking.Application.Reconciliation;
using Banking.Domain.Common;
using Banking.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Telemetry;

public sealed class OperationsOptions
{
    public const string SectionName = "Operations";

    public bool GaugesEnabled { get; set; } = true;

    public TimeSpan GaugeInterval { get; set; } = TimeSpan.FromSeconds(15);

    public bool ReconciliationEnabled { get; set; } = true;

    public TimeSpan ReconciliationInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Atualiza os gauges de outbox e transferências externas a partir do banco.</summary>
public sealed partial class OperationalGaugesWorker(
    IServiceScopeFactory scopes, IOptions<OperationsOptions> options, TimeProvider time, ILogger<OperationalGaugesWorker> logger)
    : BackgroundService
{
    public async Task CollectAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var outboxPending = await DbCommands.ScalarAsync<long>(
            db, "SELECT count(*) FROM platform.outbox_messages WHERE status = 'Pending'", cancellationToken);
        var outboxDead = await DbCommands.ScalarAsync<long>(
            db, "SELECT count(*) FROM platform.outbox_messages WHERE status = 'DeadLettered'", cancellationToken);
        var externalOpen = await DbCommands.ScalarAsync<long>(
            db, "SELECT count(*) FROM payments.external_transfers WHERE status IN ('Created', 'Unknown', 'Processing')", cancellationToken);
        var needsReview = await DbCommands.ScalarAsync<long>(
            db, "SELECT count(*) FROM payments.external_transfers WHERE requires_manual_review", cancellationToken);
        InfrastructureMetrics.UpdateGauges(outboxPending, outboxDead, externalOpen, needsReview);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.GaugesEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.GaugeInterval, time);
        do
        {
            try
            {
                await CollectAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(error);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao atualizar gauges operacionais")]
    private partial void LogFailed(Exception error);
}

/// <summary>Roda a reconciliação periodicamente. É a fonte da métrica ledger.balance_mismatch.</summary>
public sealed partial class ReconciliationWorker(
    IServiceScopeFactory scopes, IOptions<OperationsOptions> options, TimeProvider time, ILogger<ReconciliationWorker> logger)
    : BackgroundService
{
    public async Task<ReconciliationReport> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var reconciliation = scope.ServiceProvider.GetRequiredService<IReconciliationService>();
        var report = await reconciliation.RunAsync(BusinessCalendar.DateOf(time.GetUtcNow()), cancellationToken);
        foreach (var finding in report.Findings)
        {
            BankingTelemetry.BalanceMismatch.Add(1, new KeyValuePair<string, object?>("check", finding.Check));
            LogFinding(finding.Check, finding.Detail);
        }

        return report;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ReconciliationEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.ReconciliationInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(error);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliação encontrou divergência {Check}: {Detail}")]
    private partial void LogFinding(string check, string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao rodar a reconciliação")]
    private partial void LogFailed(Exception error);
}
