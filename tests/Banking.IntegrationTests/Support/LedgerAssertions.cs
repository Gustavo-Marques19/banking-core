using Xunit;

namespace Banking.IntegrationTests.Support;

/// <summary>Invariantes globais do ledger, conferidas direto no banco.</summary>
internal static class LedgerAssertions
{
    public static async Task AssertConsistentAsync(PostgresFixture postgres)
    {
        await using var connection = await Db.OpenAsync(postgres.AppConnectionString);

        var trialBalance = await Db.ScalarAsync<long>(
            connection,
            "SELECT coalesce(sum(CASE WHEN direction = 'D' THEN amount_minor ELSE -amount_minor END), 0)::bigint FROM ledger.ledger_entries");
        Assert.Equal(0, trialBalance);

        var projectionMismatches = await Db.ScalarAsync<long>(
            connection,
            """
            SELECT count(*) FROM ledger.account_balances b
             WHERE b.balance_minor <> coalesce((
                   SELECT sum(CASE WHEN e.direction = b.normal_balance THEN e.amount_minor ELSE -e.amount_minor END)
                     FROM ledger.ledger_entries e WHERE e.ledger_account_id = b.ledger_account_id), 0)
                OR b.last_sequence <> (SELECT count(*) FROM ledger.ledger_entries e WHERE e.ledger_account_id = b.ledger_account_id)
            """);
        Assert.Equal(0, projectionMismatches);

        var negativeCustomers = await Db.ScalarAsync<long>(
            connection, "SELECT count(*) FROM ledger.account_balances WHERE NOT allow_negative AND balance_minor < 0");
        Assert.Equal(0, negativeCustomers);
    }
}
