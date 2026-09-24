using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Banking.Application.Common;

/// <summary>
/// Fonte de spans e métricas da aplicação. Os nomes das métricas seguem a spec (§23).
/// </summary>
public static class BankingTelemetry
{
    public const string Name = "Banking";

    public static readonly ActivitySource Source = new(Name);

    public static readonly Meter Meter = new(Name);

    public static readonly Counter<long> TransferSuccess = Meter.CreateCounter<long>("transfer.success", description: "Transferências concluídas.");

    public static readonly Counter<long> TransferFailed = Meter.CreateCounter<long>("transfer.failed", description: "Transferências recusadas ou que falharam, por motivo.");

    public static readonly Counter<long> TransferUnknown = Meter.CreateCounter<long>("transfer.unknown", description: "Envios ao provider sem resposta conclusiva.");

    public static readonly Counter<long> TransferDuplicate = Meter.CreateCounter<long>("transfer.duplicate", description: "Pedidos repetidos respondidos pela idempotência.");

    public static readonly Counter<long> BalanceMismatch = Meter.CreateCounter<long>("ledger.balance_mismatch", description: "Divergências encontradas pela reconciliação.");

    public static readonly Counter<long> AuditChainBroken = Meter.CreateCounter<long>("audit.chain_broken", description: "Verificações da trilha de auditoria que acharam adulteração.");

    public static void RecordTransfer(string kind, string status, string? reason)
    {
        var tags = new TagList { { "kind", kind } };
        if (reason is not null)
        {
            tags.Add("reason", reason);
        }

        if (status == "completed")
        {
            TransferSuccess.Add(1, tags);
        }
        else if (status is "rejected" or "failed")
        {
            TransferFailed.Add(1, tags);
        }
    }
}
