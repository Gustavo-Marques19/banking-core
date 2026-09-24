using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Banking.Application.Audit;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banking.Infrastructure.Audit;

public static class AuditTrailBaggage
{
    /// <summary>Baggage do Activity com o correlation id da requisição.</summary>
    public const string CorrelationId = "correlation.id";
}

internal sealed class AuditTrail(BankingDbContext db, TimeProvider time) : IAuditTrail
{
    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var activity = Activity.Current;
        db.Add(new AuditLogRecord
        {
            OccurredAt = time.GetUtcNow(),
            Actor = entry.Actor.Subject,
            Operation = entry.Operation,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId.ToString(),
            Outcome = entry.Outcome,
            CorrelationId = activity?.GetBaggageItem(AuditTrailBaggage.CorrelationId) ?? activity?.TraceId.ToString(),
            TraceId = activity?.TraceId.ToString(),
            Details = JsonSerializer.Serialize(entry.Details),
        });
    }
}

/// <summary>
/// Recalcula a cadeia do zero, em C#, sem confiar em função do banco: quem adultera o banco não adultera este código.
/// O formato do payload tem que ser idêntico ao de platform.audit_entry_payload.
/// </summary>
internal sealed class AuditQueries(BankingDbContext db) : IAuditQueries
{
    private const char Separator = (char)31;

    public async Task<IReadOnlyList<AuditLogView>> ListByResourceAsync(string resourceId, int limit, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            """
            SELECT chain_position, occurred_at, actor, operation, resource_type, resource_id, outcome, trace_id, details::text
              FROM platform.audit_log
             WHERE resource_id = @resourceId
             ORDER BY chain_position
             LIMIT @limit
            """,
            cancellationToken);
        command.Parameters.AddWithValue("resourceId", resourceId);
        command.Parameters.AddWithValue("limit", limit);

        var entries = new List<AuditLogView>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add(new AuditLogView(
                    reader.GetInt64(0),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetString(8)));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return entries;
    }

    public async Task<AuditVerification> VerifyChainAsync(CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            """
            SELECT chain_position, occurred_at, actor, operation, resource_type, resource_id, outcome,
                   correlation_id, trace_id, details::text, previous_hash, hash
              FROM platform.audit_log
             ORDER BY chain_position
            """,
            cancellationToken);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            byte[]? previous = null;
            long expected = 1;
            while (await reader.ReadAsync(cancellationToken))
            {
                var position = reader.GetInt64(0);
                if (position != expected)
                {
                    return new AuditVerification(expected - 1, expected, "Posição ausente na cadeia: registro apagado.");
                }

                var storedPrevious = reader.IsDBNull(10) ? null : (byte[])reader[10];
                if (!SameBytes(storedPrevious, previous))
                {
                    return new AuditVerification(expected - 1, position, "previous_hash não confere com o registro anterior.");
                }

                var payload = string.Join(
                    Separator,
                    position.ToString(CultureInfo.InvariantCulture),
                    reader.GetFieldValue<DateTimeOffset>(1).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    reader.GetString(9));

                var computed = SHA256.HashData([.. previous ?? [], .. Encoding.UTF8.GetBytes(payload)]);
                var stored = (byte[])reader[11];
                if (!SameBytes(computed, stored))
                {
                    return new AuditVerification(expected - 1, position, "Hash não confere: conteúdo alterado.");
                }

                previous = stored;
                expected++;
            }

            return new AuditVerification(expected - 1, null, null);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static bool SameBytes(byte[]? a, byte[]? b) =>
        a is null ? b is null : b is not null && CryptographicOperations.FixedTimeEquals(a, b);

    private async Task<NpgsqlCommand> CommandAsync(string sql, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        return new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
    }
}
