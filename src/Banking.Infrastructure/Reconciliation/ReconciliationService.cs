using Banking.Application.Abstractions;
using Banking.Application.Reconciliation;
using Banking.Domain.Common;
using Banking.Domain.Ledger;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banking.Infrastructure.Reconciliation;

/// <summary>
/// Confere as invariantes de docs/ledger/lancamentos.md. Varre tudo a cada execução; em volume real,
/// a versão incremental olharia só as contas movimentadas desde a última execução.
/// </summary>
internal sealed class ReconciliationService(BankingDbContext db, IBankingProvider provider, TimeProvider time) : IReconciliationService
{
    public async Task<ReconciliationReport> RunAsync(DateOnly statementDate, CancellationToken cancellationToken)
    {
        var findings = new List<ReconciliationFinding>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();

            // Uma transação REPEATABLE READ: todas as consultas enxergam o mesmo instante.
            await using var snapshot = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
            await TrialBalanceAsync(connection, snapshot, findings, cancellationToken);
            await ProjectionAsync(connection, snapshot, findings, cancellationToken);
            await NegativeBalancesAsync(connection, snapshot, findings, cancellationToken);
            await ClearingAsync(connection, snapshot, findings, cancellationToken);
            await SettlementAsync(connection, snapshot, statementDate, findings, cancellationToken);
            await snapshot.CommitAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return new ReconciliationReport(time.GetUtcNow(), statementDate, findings);
    }

    private static async Task TrialBalanceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, List<ReconciliationFinding> findings, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT currency, sum(CASE WHEN direction = 'D' THEN amount_minor ELSE -amount_minor END)::bigint
              FROM ledger.ledger_entries
             GROUP BY currency
            HAVING sum(CASE WHEN direction = 'D' THEN amount_minor ELSE -amount_minor END) <> 0
            """,
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            findings.Add(new("trial_balance", $"Σ débitos − Σ créditos em {reader.GetString(0)} = {reader.GetInt64(1)} centavos."));
        }
    }

    private static async Task ProjectionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, List<ReconciliationFinding> findings, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT b.ledger_account_id, b.balance_minor, coalesce(x.total, 0)::bigint, b.last_sequence, coalesce(x.entries, 0)::bigint,
                   coalesce(x.max_sequence, 0)::bigint, x.last_balance_after
              FROM ledger.account_balances b
              LEFT JOIN LATERAL (
                   SELECT sum(CASE WHEN e.direction = b.normal_balance THEN e.amount_minor ELSE -e.amount_minor END) AS total,
                          count(*) AS entries,
                          max(e.account_sequence) AS max_sequence,
                          (SELECT e2.balance_after_minor FROM ledger.ledger_entries e2
                            WHERE e2.ledger_account_id = b.ledger_account_id ORDER BY e2.account_sequence DESC LIMIT 1) AS last_balance_after
                     FROM ledger.ledger_entries e
                    WHERE e.ledger_account_id = b.ledger_account_id) x ON true
             WHERE b.balance_minor <> coalesce(x.total, 0)
                OR b.last_sequence <> coalesce(x.entries, 0)
                OR b.last_sequence <> coalesce(x.max_sequence, 0)
                OR (x.last_balance_after IS NOT NULL AND x.last_balance_after <> b.balance_minor)
            """,
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            findings.Add(new(
                "balance_projection",
                $"Conta contábil {reader.GetGuid(0)}: saldo {reader.GetInt64(1)}, soma dos lançamentos {reader.GetInt64(2)}, " +
                $"sequência {reader.GetInt64(3)}, lançamentos {reader.GetInt64(4)}, maior sequência {reader.GetInt64(5)}."));
        }
    }

    private static async Task NegativeBalancesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, List<ReconciliationFinding> findings, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT ledger_account_id, balance_minor FROM ledger.account_balances WHERE NOT allow_negative AND balance_minor < 0",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            findings.Add(new("negative_balance", $"Conta contábil {reader.GetGuid(0)} com saldo {reader.GetInt64(1)}."));
        }
    }

    /// <summary>Clearing tem que ser exatamente a soma das transferências externas ainda em aberto.</summary>
    private static async Task ClearingAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, List<ReconciliationFinding> findings, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT (SELECT coalesce(sum(CASE WHEN direction = 'C' THEN amount_minor ELSE -amount_minor END), 0)
                      FROM ledger.ledger_entries WHERE ledger_account_id = @clearing)::bigint,
                   (SELECT coalesce(sum(amount_minor), 0)
                      FROM payments.external_transfers WHERE status IN ('Created', 'Unknown', 'Processing'))::bigint
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("clearing", SystemLedgerAccounts.Clearing);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var clearing = reader.GetInt64(0);
        var pending = reader.GetInt64(1);
        if (clearing != pending)
        {
            findings.Add(new("clearing_mismatch", $"Clearing {clearing} centavos, transferências em aberto {pending} centavos."));
        }
    }

    /// <summary>Liquidações do dia no ledger contra o extrato do provider, referência a referência.</summary>
    private async Task SettlementAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateOnly date,
        List<ReconciliationFinding> findings,
        CancellationToken cancellationToken)
    {
        var ours = new Dictionary<string, long>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT t.id, t.amount_minor
              FROM payments.external_transfers t
              JOIN ledger.ledger_transactions l ON l.external_id = 'external-transfer:' || t.id::text || ':resolution'
             WHERE t.status = 'Completed' AND l.effective_date = @date
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("date", date);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ours[reader.GetGuid(0).ToString("N")] = reader.GetInt64(1);
            }
        }

        IReadOnlyList<ProviderStatementLine> statement;
        try
        {
            statement = await provider.GetStatementAsync(date, cancellationToken);
        }
        catch (ProviderUnavailableException error)
        {
            findings.Add(new("settlement_unavailable", error.Message));
            return;
        }

        var theirs = statement.ToDictionary(l => l.ClientReference, l => l.Amount.MinorUnits);
        foreach (var (reference, amount) in theirs)
        {
            if (!ours.TryGetValue(reference, out var ourAmount))
            {
                // O provider liquidou e nós ainda não: pode ser transferência em UNKNOWN esperando consulta.
                var pending = await IsStillOpenAsync(connection, transaction, reference, cancellationToken);
                findings.Add(new(
                    pending ? "settlement_pending_in_ledger" : "settlement_missing_in_ledger",
                    $"Provider liquidou {reference} ({Money.FromMinor(amount, Currency.Brl)}), sem liquidação correspondente no ledger."));
            }
            else if (ourAmount != amount)
            {
                findings.Add(new("settlement_amount_mismatch", $"{reference}: ledger {ourAmount}, provider {amount} centavos."));
            }
        }

        foreach (var reference in ours.Keys.Where(r => !theirs.ContainsKey(r)))
        {
            findings.Add(new("settlement_missing_at_provider", $"Ledger liquidou {reference}, que não está no extrato do provider."));
        }
    }

    private static async Task<bool> IsStillOpenAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string reference, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(reference, "N", out var id))
        {
            return false;
        }

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM payments.external_transfers WHERE id = @id AND status IN ('Created', 'Unknown', 'Processing')",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))! > 0;
    }
}
