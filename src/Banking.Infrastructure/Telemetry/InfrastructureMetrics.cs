using System.Diagnostics.Metrics;
using Banking.Application.Common;

namespace Banking.Infrastructure.Telemetry;

/// <summary>Métricas de infraestrutura, no mesmo Meter da aplicação para sair no mesmo exporter.</summary>
public static class InfrastructureMetrics
{
    public static readonly Histogram<double> ProviderLatency = BankingTelemetry.Meter.CreateHistogram<double>(
        "provider.latency", unit: "ms", description: "Latência das chamadas ao provider.");

    public static readonly Counter<long> ProviderError = BankingTelemetry.Meter.CreateCounter<long>(
        "provider.error", description: "Chamadas ao provider que falharam.");

    public static readonly Counter<long> ProviderTimeout = BankingTelemetry.Meter.CreateCounter<long>(
        "provider.timeout", description: "Chamadas ao provider que estouraram o timeout.");

    public static readonly Counter<long> OutboxRetry = BankingTelemetry.Meter.CreateCounter<long>(
        "outbox.retry", description: "Tentativas de publicação que falharam e foram reagendadas.");

    private static long _outboxPending;
    private static long _outboxDeadLettered;
    private static long _externalOpen;
    private static long _externalNeedsReview;

    static InfrastructureMetrics()
    {
        BankingTelemetry.Meter.CreateObservableGauge("outbox.pending", () => Interlocked.Read(ref _outboxPending),
            description: "Mensagens do outbox ainda não publicadas.");
        BankingTelemetry.Meter.CreateObservableGauge("outbox.dead_lettered", () => Interlocked.Read(ref _outboxDeadLettered),
            description: "Mensagens do outbox recusadas pelo broker.");
        BankingTelemetry.Meter.CreateObservableGauge("transfer.pending", () => Interlocked.Read(ref _externalOpen),
            description: "Transferências externas em CREATED, UNKNOWN ou PROCESSING.");
        BankingTelemetry.Meter.CreateObservableGauge("transfer.needs_review", () => Interlocked.Read(ref _externalNeedsReview),
            description: "Transferências externas esperando revisão manual.");
    }

    /// <summary>Valores lidos do banco pelo coletor periódico; o gauge só devolve o último valor, sem consulta.</summary>
    public static void UpdateGauges(long outboxPending, long outboxDeadLettered, long externalOpen, long externalNeedsReview)
    {
        Interlocked.Exchange(ref _outboxPending, outboxPending);
        Interlocked.Exchange(ref _outboxDeadLettered, outboxDeadLettered);
        Interlocked.Exchange(ref _externalOpen, externalOpen);
        Interlocked.Exchange(ref _externalNeedsReview, externalNeedsReview);
    }
}
