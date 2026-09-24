using Banking.Application.Idempotency;
using Banking.Infrastructure.Persistence;
using NpgsqlTypes;

namespace Banking.Infrastructure.Platform;

internal sealed class IdempotencyStore(BankingDbContext db) : IIdempotencyStore
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public Task<IdempotencyClaim> ClaimAsync(IdempotencyRequest request, CancellationToken cancellationToken) =>
        PostgresErrors.TranslateAsync(async () =>
        {
            // Com outra transação segurando a mesma chave, o INSERT espera o commit dela (ADR-005).
            await using (var insert = DbCommands.InTransaction(
                db,
                """
                INSERT INTO platform.idempotency_keys (client_id, operation, key, request_hash, created_at, expires_at)
                VALUES (@client, @operation, @key, @hash, now(), now() + @retention)
                ON CONFLICT DO NOTHING
                """))
            {
                AddKey(insert, request);
                insert.Parameters.AddWithValue("hash", request.RequestHash);
                insert.Parameters.AddWithValue("retention", Retention);
                if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
                {
                    return new IdempotencyClaim(IdempotencyClaimStatus.Claimed);
                }
            }

            await using var select = DbCommands.InTransaction(
                db,
                "SELECT request_hash, result::text FROM platform.idempotency_keys WHERE client_id = @client AND operation = @operation AND key = @key");
            AddKey(select, request);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Chave de idempotência sumiu entre o INSERT e o SELECT.");
            }

            var storedHash = (byte[])reader[0];
            var result = reader.IsDBNull(1) ? null : reader.GetString(1);
            return storedHash.AsSpan().SequenceEqual(request.RequestHash) && result is not null
                ? new IdempotencyClaim(IdempotencyClaimStatus.Replay, result)
                : new IdempotencyClaim(IdempotencyClaimStatus.Mismatch);
        });

    public Task CompleteAsync(IdempotencyRequest request, string resultJson, CancellationToken cancellationToken) =>
        PostgresErrors.TranslateAsync(async () =>
        {
            await using var command = DbCommands.InTransaction(
                db,
                "UPDATE platform.idempotency_keys SET result = @result WHERE client_id = @client AND operation = @operation AND key = @key");
            AddKey(command, request);
            command.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, resultJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        });

    private static void AddKey(Npgsql.NpgsqlCommand command, IdempotencyRequest request)
    {
        command.Parameters.AddWithValue("client", request.ClientId);
        command.Parameters.AddWithValue("operation", request.Operation);
        command.Parameters.AddWithValue("key", request.Key);
    }
}
