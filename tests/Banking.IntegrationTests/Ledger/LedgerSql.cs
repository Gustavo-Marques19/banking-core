using Banking.Domain.Ledger;
using Npgsql;
using Xunit;

namespace Banking.IntegrationTests.Ledger;

/// <summary>Escreve no ledger por SQL cru, como faria alguém com a credencial da aplicação.</summary>
internal static class LedgerSql
{
    public static async Task<Guid> OpenCustomerLedgerAccountAsync(NpgsqlConnection connection, bool allowNegative = false)
    {
        var id = Guid.NewGuid();
        await Db.ExecuteAsync(
            connection,
            """
            INSERT INTO ledger.ledger_accounts
                (id, code, name, nature, normal_balance, currency, account_id, allow_negative, has_materialized_balance, created_at)
            VALUES (@id, @code, 'conta de teste', 'LIABILITY', 'C', 'BRL', @accountId, @allowNegative, true, now())
            """,
            null,
            ("id", id),
            ("code", "2.1.T" + id.ToString("N")),
            ("accountId", Guid.NewGuid()),
            ("allowNegative", allowNegative));
        return id;
    }

    public static async Task<Guid> InsertTransactionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid? reverses = null)
    {
        var id = Guid.NewGuid();
        await Db.ExecuteAsync(
            connection,
            """
            INSERT INTO ledger.ledger_transactions (id, external_id, type, description, posted_at, effective_date, reverses_transaction_id)
            VALUES (@id, @externalId, 'Deposit', 'teste', now(), current_date, @reverses)
            """,
            transaction,
            ("id", id),
            ("externalId", "sql-" + id.ToString("N")),
            ("reverses", (object?)reverses ?? DBNull.Value));
        return id;
    }

    public static Task InsertEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid transactionId,
        Guid ledgerAccountId,
        char direction,
        long amountMinor,
        string currency = "BRL") =>
        Db.ExecuteAsync(
            connection,
            """
            INSERT INTO ledger.ledger_entries (id, transaction_id, ledger_account_id, direction, amount_minor, currency)
            VALUES (@id, @transactionId, @accountId, @direction, @amount, @currency)
            """,
            transaction,
            ("id", Guid.NewGuid()),
            ("transactionId", transactionId),
            ("accountId", ledgerAccountId),
            ("direction", direction.ToString()),
            ("amount", amountMinor),
            ("currency", currency));

    /// <summary>Depósito completo e commitado: D Funding / C conta.</summary>
    public static async Task<Guid> DepositAsync(NpgsqlConnection connection, Guid ledgerAccountId, long amountMinor)
    {
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var id = await InsertTransactionAsync(connection, transaction);
        await InsertEntryAsync(connection, transaction, id, SystemLedgerAccounts.Funding, 'D', amountMinor);
        await InsertEntryAsync(connection, transaction, id, ledgerAccountId, 'C', amountMinor);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        return id;
    }

    public static Task<long> BalanceAsync(NpgsqlConnection connection, Guid ledgerAccountId) =>
        Db.ScalarAsync<long>(
            connection,
            "SELECT balance_minor FROM ledger.account_balances WHERE ledger_account_id = @id",
            null,
            ("id", ledgerAccountId));
}
