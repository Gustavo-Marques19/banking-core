using Banking.Domain.Ledger;
using Npgsql;
using Xunit;

namespace Banking.IntegrationTests.Ledger;

/// <summary>
/// Critério do M2: nem com SQL cru e a credencial da aplicação dá para deixar o ledger inconsistente (ADR-008).
/// </summary>
public sealed class LedgerDatabaseTests(PostgresFixture postgres)
{
    private const string IntegrityViolation = "23000";
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";
    private const string UniqueViolation = "23505";
    private const string InsufficientPrivilege = "42501";
    private const string LockNotAvailable = "55P03";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Contas_de_sistema_existem()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);

        var count = await Db.ScalarAsync<long>(
            app,
            "SELECT count(*) FROM ledger.ledger_accounts WHERE id = ANY(@ids) AND account_id IS NULL",
            null,
            ("ids", new[] { SystemLedgerAccounts.Funding, SystemLedgerAccounts.Settlement, SystemLedgerAccounts.Clearing }));

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Deposito_atualiza_saldo_sequencia_e_saldo_apos()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(app);

        await LedgerSql.DepositAsync(app, customer, 100_000);
        await LedgerSql.DepositAsync(app, customer, 50);

        Assert.Equal(100_050, await LedgerSql.BalanceAsync(app, customer));
        await using var command = new NpgsqlCommand(
            "SELECT account_sequence, balance_after_minor FROM ledger.ledger_entries WHERE ledger_account_id = @id ORDER BY account_sequence",
            app);
        command.Parameters.AddWithValue("id", customer);
        var rows = new List<(long Sequence, long BalanceAfter)>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                rows.Add((reader.GetInt64(0), reader.GetInt64(1)));
            }
        }

        Assert.Equal([(1L, 100_000L), (2L, 100_050L)], rows);
    }

    [Fact]
    public async Task Lancamento_desbalanceado_falha_no_commit()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(app);

        await using var transaction = await app.BeginTransactionAsync(Ct);
        var id = await LedgerSql.InsertTransactionAsync(app, transaction);
        await LedgerSql.InsertEntryAsync(app, transaction, id, SystemLedgerAccounts.Funding, 'D', 10_000);
        await LedgerSql.InsertEntryAsync(app, transaction, id, customer, 'C', 9_000);

        await Db.AssertFailsAsync(() => transaction.CommitAsync(Ct), IntegrityViolation);
        await using var check = await Db.OpenAsync(postgres.AppConnectionString);
        Assert.Equal(0, await LedgerSql.BalanceAsync(check, customer));
    }

    [Fact]
    public async Task Transacao_com_um_lancamento_falha_no_commit()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(app);

        await using var transaction = await app.BeginTransactionAsync(Ct);
        var id = await LedgerSql.InsertTransactionAsync(app, transaction);
        await LedgerSql.InsertEntryAsync(app, transaction, id, customer, 'C', 10_000);

        await Db.AssertFailsAsync(() => transaction.CommitAsync(Ct), IntegrityViolation);
    }

    [Fact]
    public async Task Transacao_sem_lancamentos_falha_no_commit()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);

        await using var transaction = await app.BeginTransactionAsync(Ct);
        await LedgerSql.InsertTransactionAsync(app, transaction);

        await Db.AssertFailsAsync(() => transaction.CommitAsync(Ct), IntegrityViolation);
    }

    [Fact]
    public async Task Saldo_de_cliente_nao_fica_negativo()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(app);
        await LedgerSql.DepositAsync(app, customer, 5_000);

        await using var transaction = await app.BeginTransactionAsync(Ct);
        var id = await LedgerSql.InsertTransactionAsync(app, transaction);
        await LedgerSql.InsertEntryAsync(app, transaction, id, SystemLedgerAccounts.Funding, 'C', 5_001);

        await Db.AssertFailsAsync(
            () => LedgerSql.InsertEntryAsync(app, transaction, id, customer, 'D', 5_001), CheckViolation);
    }

    [Fact]
    public async Task Lancamento_em_transacao_contabil_antiga_falha()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(app);
        var old = await LedgerSql.DepositAsync(app, customer, 1_000);

        await using var transaction = await app.BeginTransactionAsync(Ct);

        await Db.AssertFailsAsync(
            () => LedgerSql.InsertEntryAsync(app, transaction, old, SystemLedgerAccounts.Funding, 'D', 500), IntegrityViolation);
    }

    [Fact]
    public async Task Sequencia_e_saldo_enviados_pela_aplicacao_sao_ignorados()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(app);

        await using (var transaction = await app.BeginTransactionAsync(Ct))
        {
            var id = await LedgerSql.InsertTransactionAsync(app, transaction);
            await LedgerSql.InsertEntryAsync(app, transaction, id, SystemLedgerAccounts.Funding, 'D', 700);
            await Db.ExecuteAsync(
                app,
                """
                INSERT INTO ledger.ledger_entries
                    (id, transaction_id, ledger_account_id, direction, amount_minor, currency, account_sequence, balance_after_minor)
                VALUES (@entryId, @transactionId, @accountId, 'C', 700, 'BRL', 999, 999999999)
                """,
                transaction,
                ("entryId", Guid.NewGuid()),
                ("transactionId", id),
                ("accountId", customer));
            await transaction.CommitAsync(Ct);
        }

        var (sequence, balanceAfter) = await SingleEntryAsync(app, customer);
        Assert.Equal(1, sequence);
        Assert.Equal(700, balanceAfter);
        Assert.Equal(700, await LedgerSql.BalanceAsync(app, customer));
    }

    [Fact]
    public async Task App_nao_altera_nem_apaga_o_ledger()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(app);
        await LedgerSql.DepositAsync(app, customer, 1_000);

        foreach (var sql in new[]
        {
            "UPDATE ledger.ledger_entries SET amount_minor = 1 WHERE ledger_account_id = @id",
            "DELETE FROM ledger.ledger_entries WHERE ledger_account_id = @id",
            "UPDATE ledger.ledger_transactions SET description = 'x'",
            "DELETE FROM ledger.ledger_transactions",
            "UPDATE ledger.ledger_accounts SET allow_negative = true WHERE id = @id",
            "UPDATE ledger.account_balances SET balance_minor = 999999 WHERE ledger_account_id = @id",
            "INSERT INTO ledger.account_balances (ledger_account_id, currency, normal_balance, balance_minor, last_sequence, allow_negative, updated_at) VALUES (@id, 'BRL', 'C', 1, 0, true, now())",
            "TRUNCATE ledger.ledger_entries",
        })
        {
            await Db.AssertFailsAsync(() => Db.ExecuteAsync(app, sql, null, ("id", customer)), InsufficientPrivilege);
        }
    }

    [Fact]
    public async Task Nem_o_dono_das_tabelas_altera_o_ledger()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(app);
        await LedgerSql.DepositAsync(app, customer, 1_000);
        await using var owner = await Db.OpenAsync(postgres.MigratorConnectionString);

        foreach (var sql in new[]
        {
            "UPDATE ledger.ledger_entries SET amount_minor = 1 WHERE ledger_account_id = @id",
            "DELETE FROM ledger.ledger_entries WHERE ledger_account_id = @id",
            "UPDATE ledger.account_balances SET balance_minor = 999999 WHERE ledger_account_id = @id",
            "DELETE FROM ledger.account_balances WHERE ledger_account_id = @id",
            "TRUNCATE ledger.ledger_entries CASCADE",
        })
        {
            await Db.AssertFailsAsync(() => Db.ExecuteAsync(owner, sql, null, ("id", customer)), IntegrityViolation);
        }

        Assert.Equal(1_000, await LedgerSql.BalanceAsync(app, customer));
    }

    [Fact]
    public async Task App_nao_cria_conta_de_sistema()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);

        await Db.AssertFailsAsync(
            () => Db.ExecuteAsync(
                app,
                """
                INSERT INTO ledger.ledger_accounts
                    (id, code, name, nature, normal_balance, currency, account_id, allow_negative, has_materialized_balance, created_at)
                VALUES (@id, @code, 'impressora de dinheiro', 'ASSET', 'D', 'BRL', NULL, true, false, now())
                """,
                null,
                ("id", Guid.NewGuid()),
                ("code", "9." + Guid.NewGuid().ToString("N"))),
            InsufficientPrivilege);
    }

    [Fact]
    public async Task Conta_de_cliente_nao_pode_permitir_saldo_negativo()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);

        await Db.AssertFailsAsync(() => LedgerSql.OpenCustomerLedgerAccountAsync(app, allowNegative: true), CheckViolation);
    }

    [Fact]
    public async Task Lancamento_precisa_estar_na_moeda_da_conta()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(app);

        await using var transaction = await app.BeginTransactionAsync(Ct);
        var id = await LedgerSql.InsertTransactionAsync(app, transaction);

        await Db.AssertFailsAsync(
            () => LedgerSql.InsertEntryAsync(app, transaction, id, customer, 'C', 100, currency: "USD"), ForeignKeyViolation);
    }

    [Fact]
    public async Task Transacao_so_pode_ser_estornada_uma_vez()
    {
        await using var app = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(app);
        var original = await LedgerSql.DepositAsync(app, customer, 2_000);

        await ReverseDepositAsync(app, original, customer, 1_000);

        await Db.AssertFailsAsync(() => ReverseDepositAsync(app, original, customer, 1_000), UniqueViolation);
    }

    [Fact]
    public async Task Lock_de_saldo_segura_a_linha_ate_o_fim_da_transacao()
    {
        await using var first = await Db.OpenAsync(postgres.AppConnectionString);
        var customer = await LedgerSql.OpenCustomerLedgerAccountAsync(first);
        await LedgerSql.DepositAsync(first, customer, 3_000);

        await using var holding = await first.BeginTransactionAsync(Ct);
        var locked = await Db.ScalarAsync<long>(
            first, "SELECT balance_minor FROM ledger.lock_balances(@ids)", holding, ("ids", new[] { customer }));
        Assert.Equal(3_000, locked);

        await using var second = await Db.OpenAsync(postgres.AppConnectionString);
        await using var waiting = await second.BeginTransactionAsync(Ct);
        await Db.ExecuteAsync(second, "SET LOCAL lock_timeout = '200ms'", waiting);

        await Db.AssertFailsAsync(
            () => Db.ExecuteAsync(second, "SELECT * FROM ledger.lock_balances(@ids)", waiting, ("ids", new[] { customer })),
            LockNotAvailable);
    }

    private static async Task ReverseDepositAsync(NpgsqlConnection app, Guid original, Guid customer, long amountMinor)
    {
        await using var transaction = await app.BeginTransactionAsync(Ct);
        var id = await LedgerSql.InsertTransactionAsync(app, transaction, reverses: original);
        await LedgerSql.InsertEntryAsync(app, transaction, id, customer, 'D', amountMinor);
        await LedgerSql.InsertEntryAsync(app, transaction, id, SystemLedgerAccounts.Funding, 'C', amountMinor);
        await transaction.CommitAsync(Ct);
    }

    private static async Task<(long Sequence, long BalanceAfter)> SingleEntryAsync(NpgsqlConnection connection, Guid ledgerAccountId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT account_sequence, balance_after_minor FROM ledger.ledger_entries WHERE ledger_account_id = @id", connection);
        command.Parameters.AddWithValue("id", ledgerAccountId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        var result = (reader.GetInt64(0), reader.GetInt64(1));
        Assert.False(await reader.ReadAsync(Ct));
        return result;
    }
}
